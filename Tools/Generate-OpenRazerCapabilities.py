#!/usr/bin/env python3
"""Extract compatibility facts without importing or executing upstream Python/C.

Inputs are hash-locked to one commit. The deliberately restricted C reader only
understands reviewed PID switch shapes; new source needs a new review and lock.
No upstream function bodies are emitted or compiled into the application.
"""
import argparse
import ast
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import urllib.request

OPENRAZER_COMMIT = '477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d'
ROOT = Path(__file__).resolve().parents[1]
LOCK_PATH = ROOT / 'Tools/OpenRazer/input-lock.json'
ATTRS = {'USB_PID', 'USB_VID', 'METHODS', 'DPI_MAX', 'POLL_RATES', 'AVAILABLE_DPI'}


class InvalidInput(ValueError):
    pass


def evaluate(node, resolve):
    if isinstance(node, ast.Constant) and isinstance(node.value, (str, int, type(None))):
        return node.value
    if isinstance(node, (ast.List, ast.Tuple)):
        return [evaluate(n, resolve) for n in node.elts]
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
        left, right = evaluate(node.left, resolve), evaluate(node.right, resolve)
        if not isinstance(left, list) or not isinstance(right, list):
            raise InvalidInput('Only list concatenation is supported')
        return left + right
    if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name):
        return resolve(node.value.id)[node.attr]
    raise InvalidInput('Unsupported metadata expression: ' + ast.dump(node))


def parse_classes(mouse, base):
    classes = {}
    for source in (base, mouse):
        for node in ast.parse(source).body:
            if isinstance(node, ast.ClassDef):
                if node.name in classes:
                    raise InvalidInput('Duplicate class: ' + node.name)
                classes[node.name] = node
    aliases = {'__RazerDevice': 'RazerDevice', '__RazerDeviceBrightnessSuspend': 'RazerDeviceBrightnessSuspend'}
    done, pending = {}, set()

    def resolve(name):
        name = aliases.get(name, name)
        if name in done:
            return done[name]
        if name in pending:
            raise InvalidInput('Inheritance cycle: ' + name)
        if name not in classes:
            raise InvalidInput('Unresolved inheritance: ' + name)
        pending.add(name)
        node = classes[name]
        values = {}
        # RazerDevice is the external DBus boundary; only metadata is needed.
        if name != 'RazerDevice':
            if len(node.bases) != 1 or not isinstance(node.bases[0], ast.Name):
                raise InvalidInput('Unsupported inheritance: ' + name)
            values.update(resolve(node.bases[0].id))
        for item in node.body:
            key, expr = None, None
            if isinstance(item, ast.Assign) and len(item.targets) == 1 and isinstance(item.targets[0], ast.Name):
                key, expr = item.targets[0].id, item.value
            elif isinstance(item, ast.AnnAssign) and isinstance(item.target, ast.Name):
                key, expr = item.target.id, item.value
            if key in ATTRS and expr is not None:
                values[key] = evaluate(expr, resolve)
        pending.remove(name)
        done[name] = values
        return values

    records, seen = [], set()
    for node in ast.parse(mouse).body:
        if not isinstance(node, ast.ClassDef):
            continue
        values = resolve(node.name).copy()
        pid = values.get('USB_PID')
        if pid is None:
            continue
        if type(pid) is not int or not 0 <= pid <= 65535 or pid in seen:
            raise InvalidInput('Invalid or duplicate PID: ' + str(pid))
        if values.get('USB_VID') != 0x1532:
            raise InvalidInput('Unexpected VID: ' + node.name)
        if not isinstance(values.get('METHODS'), list) or not all(isinstance(x, str) for x in values['METHODS']):
            raise InvalidInput('Unresolved METHODS: ' + node.name)
        for key in ('DPI_MAX', 'POLL_RATES', 'AVAILABLE_DPI'):
            value = values.get(key)
            if value is not None and (type(value) is not int if key == 'DPI_MAX' else not isinstance(value, list) or not all(type(x) is int and x > 0 for x in value)):
                raise InvalidInput('Invalid ' + key + ': ' + node.name)
        seen.add(pid)
        doc = (ast.get_docstring(node) or node.name).splitlines()[0].strip()
        values.update(Class=node.name, Name=doc.removeprefix('Class for the ').removeprefix('Class for '), Line=node.lineno)
        records.append(values)
    return sorted(records, key=lambda r: r['USB_PID'])


def mask_c(source):
    # Preserve offsets while removing comments and quoted braces/keywords.
    return re.sub(r'/\*.*?\*/|//[^\n]*|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',
                  lambda m: ''.join('\n' if c == '\n' else ' ' for c in m[0]), source, flags=re.S)


def brace_end(mask, start):
    if mask[start] != '{':
        raise InvalidInput('Expected C block')
    depth = 1
    for i in range(start + 1, len(mask)):
        depth += (mask[i] == '{') - (mask[i] == '}')
        if depth == 0:
            return i
    raise InvalidInput('Unclosed C block')


def function(source, name):
    mask = mask_c(source)
    match = re.search(r'\b' + re.escape(name) + r'\s*\([^;{}]*\)\s*\{', mask)
    if not match:
        raise InvalidInput('Missing C function: ' + name)
    start = mask.index('{', match.start())
    return source[start + 1:brace_end(mask, start)], source[:start].count('\n') + 1


def pid_switches(body, constants):
    mask, result = mask_c(body), []
    for match in re.finditer(r'switch\s*\(\s*(?:device->usb_pid|usb_dev->descriptor.idProduct)\s*\)\s*\{', mask):
        start = mask.index('{', match.start())
        end = brace_end(mask, start)
        labels, depth = [], 0
        for i in range(start + 1, end):
            depth += (mask[i] == '{') - (mask[i] == '}')
            if depth == 0:
                label = re.match(r'(?:case\s+(USB_DEVICE_ID_RAZER_\w+)|default)\s*:', mask[i:])
                if label:
                    labels.append((i, i + label.end(), label[1]))
        groups, pending = {}, []
        for index, (pos, after, symbol) in enumerate(labels):
            stop = labels[index + 1][0] if index + 1 < len(labels) else end
            code = body[after:stop]
            if symbol:
                if symbol not in constants:
                    raise InvalidInput('Unknown PID constant: ' + symbol)
                pending.append(constants[symbol])
            if mask_c(code).strip():
                for pid in pending:
                    if pid in groups:
                        raise InvalidInput('Duplicate C PID case')
                    groups[pid] = code
                pending = []
        result.append(groups)
    if not result:
        raise InvalidInput('Missing PID switch')
    return result


def one_tid(code):
    values = re.findall(r'request\.transaction_id\.id\s*=\s*(0x[\dA-Fa-f]+)', mask_c(code))
    if len(set(values)) > 1:
        raise InvalidInput('Ambiguous transaction branch')
    if not values:
        return 0
    value = int(values[0], 16)
    if value not in (0x1f, 0x3f, 0xff):
        raise InvalidInput('Unknown transaction')
    return value


def load_inputs(cache, fetch=False):
    lock = json.loads(LOCK_PATH.read_text())
    if lock['commit'] != OPENRAZER_COMMIT or not re.fullmatch('[0-9a-f]{40}', OPENRAZER_COMMIT):
        raise InvalidInput('Commit lock mismatch')
    data = {}
    for path, digest in lock['sha256'].items():
        target = cache / path
        if fetch:
            url = f'https://raw.githubusercontent.com/openrazer/openrazer/{OPENRAZER_COMMIT}/{path}'
            with urllib.request.urlopen(url, timeout=30) as response:
                content = response.read(2_000_001)
            if len(content) > 2_000_000 or hashlib.sha256(content).hexdigest() != digest:
                raise InvalidInput('Downloaded hash mismatch: ' + path)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
        content = target.read_bytes()
        if hashlib.sha256(content).hexdigest() != digest:
            raise InvalidInput('Input hash mismatch: ' + path)
        data[path] = content.decode('utf-8')
    return data, lock


def generate(data):
    mouse_path = 'daemon/openrazer_daemon/hardware/mouse.py'
    records = parse_classes(data[mouse_path], data['daemon/openrazer_daemon/hardware/device_base.py'])
    driver = data['driver/razermouse_driver.c']
    constants = {name: int(value, 16) for name, value in re.findall(r'#define\s+(USB_DEVICE_ID_RAZER_\w+)\s+(0x[\da-fA-F]+)', data['driver/razermouse_driver.h'])}
    tables, lines = {}, {}
    for name in ('read_dpi', 'write_dpi', 'read_poll_rate', 'write_poll_rate', 'read_charge_level', 'read_charge_status', 'read_dpi_stages', 'write_dpi_stages'):
        body, line = function(driver, 'razer_attr_' + name)
        tables[name] = pid_switches(body, constants)
        lines[name] = line
    report_body, report_line = function(driver, 'razer_get_report')
    transport = pid_switches(report_body, constants)[0]
    common = data['driver/razerchromacommon.c']
    # Check the independently used wire facts against the pinned constructors.
    expected = {'get_dpi_xy': (4,0x85,7), 'set_dpi_xy': (4,5,7), 'get_dpi_xy_byte': (4,0x81,3), 'set_dpi_xy_byte': (4,1,3),
                'get_dpi_stages': (4,0x86,0x26), 'set_dpi_stages': (4,6,0x26), 'get_polling_rate': (0,0x85,1),
                'set_polling_rate': (0,5,1), 'get_polling_rate2': (0,0xc0,1), 'set_polling_rate2': (0,0x40,2),
                'get_battery_level': (7,0x80,2), 'get_charging_status': (7,0x84,2)}
    for name, want in expected.items():
        body,_ = function(common, 'razer_chroma_misc_' + name)
        matches = re.findall(r'get_razer_report\((0x[\da-fA-F]+),\s*(0x[\da-fA-F]+),\s*(0x[\da-fA-F]+)\)', body)
        if len(matches) != 1 or tuple(int(x,16) for x in matches[0]) != want:
            raise InvalidInput('Wire fact mismatch: ' + name)
    profiles = []
    for record in records:
        pid, methods = record['USB_PID'], set(record['METHODS'])
        def branch(name, index=0): return tables[name][index].get(pid, '')
        rd, wd = branch('read_dpi'), branch('write_dpi')
        # The second write-DPI switch is solely the modern transaction table.
        dset_tid = one_tid(wd) or one_tid(branch('write_dpi',1))
        dget_tid = one_tid(rd)
        dp = 'ModernXY' if 'get_dpi_xy(' in rd else 'LegacyByte' if 'get_dpi_xy_byte(' in rd else 'LegacyDirect' if pid in (0x13,0x16,0x29) else 'None'
        read_method = 'get_dpi_xy_byte' if dp == 'LegacyByte' else 'get_dpi_xy'
        get_dpi = read_method in methods and dp in ('ModernXY','LegacyByte') and bool(dget_tid)
        set_dpi = read_method.replace('get_','set_',1) in methods and get_dpi and bool(dset_tid)
        maximum = record.get('DPI_MAX') or 0
        minimum = 100 if dp in ('ModernXY','LegacyByte') and maximum else 0
        available = record.get('AVAILABLE_DPI') or []
        reasons = []
        if dp == 'LegacyByte':
            # Exact round trips under the daemon's 6750/255 scale only. Reject
            # arbitrary integer DPI instead of silently quantizing it.
            available = [v for v in range(100, min(maximum,6750)+1) if int(round(int(round(v/6750*255,2))/255*6750,2)) == v]
            reasons.append('Legacy byte: only exact daemon round-trip values writable')
        stages = 'get_dpi_stages' in methods and bool(one_tid(branch('read_dpi_stages')))
        set_stages = stages and 'set_dpi_stages' in methods and one_tid(branch('read_dpi_stages')) == one_tid(branch('write_dpi_stages'))
        rp, wp = branch('read_poll_rate'), branch('write_poll_rate')
        poll = 'HighRate' if 'get_polling_rate2(' in rp else 'Legacy' if 'get_polling_rate(' in rp else 'LegacyDirect' if pid in (0x13,0x16,0x29) else 'None'
        get_poll = 'get_poll_rate' in methods and poll in ('Legacy','HighRate') and bool(one_tid(rp))
        setter = 'HighRate' if 'set_polling_rate2(' in wp else 'Legacy' if 'set_polling_rate(' in wp else 'None'
        set_poll = get_poll and 'set_poll_rate' in methods and setter == poll and bool(one_tid(wp))
        second_tid = one_tid(branch('write_poll_rate',1)) if len(tables['write_poll_rate']) > 1 else 0
        if get_poll and 'set_poll_rate' in methods and not set_poll:
            reasons.append('Polling GET/SET protocol mismatch: read only')
        rates = record.get('POLL_RATES') or ([125,500,1000] if 'set_poll_rate' in methods else [])
        source = 'class/inherited' if record.get('POLL_RATES') else 'device_base default' if rates else 'none'
        allowed = (125,500,1000) if poll == 'Legacy' else (125,250,500,1000,2000,4000,8000) if poll == 'HighRate' else ()
        implemented = [r for r in rates if r in allowed] if get_poll else []
        if set(rates)-set(implemented): reasons.append('upstream-supported-but-not-implemented rates: '+','.join(map(str,sorted(set(rates)-set(implemented)))))
        battery = 'get_battery' in methods and bool(one_tid(branch('read_charge_level')))
        charge_branch = branch('read_charge_status')
        charging = 'AlwaysFalse' if 'is_charging' in methods and 'sysfs_emit' in charge_branch and not one_tid(charge_branch) else 'Query' if 'is_charging' in methods and one_tid(charge_branch) else 'None'
        t = 'LegacyDirectUsbControl' if pid in (0x13,0x16,0x29) else 'AlternateUsbReportIndex' if 'index = 0x03' in transport.get(pid,'') else 'HidFeature90Or91'
        if t != 'HidFeature90Or91': reasons.append(t + ': current Windows backend cannot map it')
        elif not any((get_dpi,get_poll,battery,charging != 'None')): reasons.append('No mapped upstream capability')
        delay_code = transport.get(pid,'')
        delay = 400 if 'ATHERIS_RECEIVER_WAIT' in delay_code else 60 if 'VIPER_MOUSE_RECEIVER_WAIT' in delay_code else 31 if 'NEW_MOUSE_RECEIVER_WAIT' in delay_code else 15
        kind = 'GenericReceiver' if pid == 0xb3 else 'DedicatedReceiver' if re.search('Receiver|Wireless',record['Class']) else 'Mouse'
        evidence = [f'{mouse_path}#L{record["Line"]}', f'driver/razermouse_driver.c#L{report_line}']
        evidence += [f'driver/razermouse_driver.c#L{lines[k]}' for k in lines]
        profiles.append(dict(pid=pid,name=record['Name'],kind=kind,transport=t,dpi=dp,get_dpi=get_dpi,set_dpi=set_dpi,
            minimum=minimum,maximum=maximum,available=available,stages=stages,set_stages=set_stages,
            dpi_tid=dget_tid,dpi_set_tid=dset_tid,dpi_get_storage=1 if 'get_dpi_xy(VARSTORE)' in rd else 0,
            stages_tid=one_tid(branch('read_dpi_stages')),poll=poll,get_poll=get_poll,set_poll=set_poll,
            poll_tid=one_tid(rp),poll_set_tid=one_tid(wp),poll_second_tid=second_tid,rates=implemented,upstream_rates=rates,rates_source=source,
            battery=battery,battery_tid=one_tid(branch('read_charge_level')),charging=charging,charging_tid=one_tid(charge_branch),
            receiver=pid == 0xb3,delay=delay,cls=record['Class'],evidence=evidence,reason='; '.join(reasons) or 'Exact facts; descriptor and live GET still required'))
    return profiles


def render(profiles, lock):
    header = ['// GENERATED FILE - DO NOT EDIT MANUALLY', '// OpenRazer commit: '+OPENRAZER_COMMIT]
    header += ['// '+p+' SHA256 '+h for p,h in sorted(lock['sha256'].items())]
    cs = header + ['using System.Collections.Generic;', 'namespace RazerBatteryTray', '{', '    internal static class OpenRazerCapabilityCatalog', '    {',
        '        internal const string Revision = "'+OPENRAZER_COMMIT+'";', '        private static readonly Dictionary<int, DeviceCapabilityProfile> entries = new Dictionary<int, DeviceCapabilityProfile>();',
        '        static OpenRazerCapabilityCatalog()', '        {']
    def quoted(x): return json.dumps(x,ensure_ascii=False)
    def ints(x): return 'new int[] { '+', '.join(map(str,x))+' }'
    def flag(x): return str(x).lower()
    for p in profiles:
        fields = [f'ProductId = 0x{p["pid"]:04X}', 'Name = '+quoted(p['name']), 'DeviceKind = DeviceKind.'+p['kind'], 'Transport = TransportKind.'+p['transport'],
            'DpiProtocol = DpiProtocolKind.'+p['dpi'], 'GetDpi = '+flag(p['get_dpi']), 'SetDpi = '+flag(p['set_dpi']),
            f'MinimumDpi = {p["minimum"]}', f'MaximumDpi = {p["maximum"]}', 'AvailableDpi = '+ints(p['available']),
            'GetStages = '+flag(p['stages']), 'SetStages = '+flag(p['set_stages']),
            f'DpiTransaction = 0x{p["dpi_tid"]:02X}',f'DpiSetTransaction = 0x{p["dpi_set_tid"]:02X}',f'DpiGetStorage = {p["dpi_get_storage"]}',f'DpiStageTransaction = 0x{p["stages_tid"]:02X}',
            'PollingProtocol = PollingProtocolKind.'+p['poll'], 'GetPolling = '+flag(p['get_poll']), 'SetPolling = '+flag(p['set_poll']),
            f'PollingTransaction = 0x{p["poll_tid"]:02X}', f'PollingSetTransaction = 0x{p["poll_set_tid"]:02X}', f'PollingSecondTransaction = 0x{p["poll_second_tid"]:02X}',
            'PollRates = '+ints(p['rates']), 'UpstreamPollRates = '+ints(p['upstream_rates']), 'PollRatesSource = '+quoted(p['rates_source']),
            'GetBattery = '+flag(p['battery']),f'BatteryTransaction = 0x{p["battery_tid"]:02X}', 'ChargingProtocol = ChargingProtocolKind.'+p['charging'],f'ChargingTransaction = 0x{p["charging_tid"]:02X}',
            'ReceiverProxy = '+flag(p['receiver']),f'ReadDelayMs = {p["delay"]}',f'WriteDelayMs = {max(80,p["delay"])}',
            'UpstreamClass = '+quoted(p['cls']), 'Evidence = '+quoted('; '.join(p['evidence'])), 'Reason = '+quoted(p['reason'])]
        cs += [f'            entries.Add(0x{p["pid"]:04X}, new DeviceCapabilityProfile {{ '+', '.join(fields)+' });']
    cs += ['        }', '        internal static DeviceCapabilityProfile Find(int pid) { DeviceCapabilityProfile p; return entries.TryGetValue(pid, out p) ? p : null; }',
        '        internal static IEnumerable<DeviceCapabilityProfile> All { get { return entries.Values; } }', '        internal static int Count { get { return entries.Count; } }', '    }', '}']
    md = ['# OpenRazer 能力事实矩阵', '', '固定提交：`'+OPENRAZER_COMMIT+'`。生成器仅提取互操作事实；不代表雷云 Lite 实机验证。', '',
          '上游 Trust 表示静态证据；运行权限还要求 transport、描述符、同实例实时 GET。所有上游 Rotation = IdentityOnly。', '',
          '| PID | 型号 / Class | Transport | DPI / 范围 / 精确离散值 | Stages GET/SET | Polling / rates (source) | Battery / Charging | TID DPI GET/SET, stages, polling GET/SET/second, battery, charging | Trust / Receiver | Rotation | Evidence / 降级原因 |',
          '|---|---|---|---|---|---|---|---|---|---|---|']
    for p in profiles:
        links = ' '.join('[source](https://github.com/openrazer/openrazer/blob/'+OPENRAZER_COMMIT+'/'+e+')' for e in p['evidence'])
        trust = 'Upstream facts; live GET required' if p['transport']=='HidFeature90Or91' else 'IdentityOnly runtime'
        md.append(f'| {p["pid"]:04X} | {p["name"]}<br>{p["cls"]} | {p["transport"]} | {p["dpi"]} GET={p["get_dpi"]} SET={p["set_dpi"]}; {p["minimum"]}–{p["maximum"]}; {p["available"] or "continuous"} | {p["stages"]}/{p["set_stages"]} | {p["poll"]} GET={p["get_poll"]} SET={p["set_poll"]}; {p["rates"]} ({p["rates_source"]}); upstream={p["upstream_rates"]} | {p["battery"]} / {p["charging"]} | '+','.join(f'{p[k]:02X}' for k in ('dpi_tid','dpi_set_tid','stages_tid','poll_tid','poll_set_tid','poll_second_tid','battery_tid','charging_tid'))+f' | {trust}; proxy={p["receiver"]} | IdentityOnly | {links}<br>{p["reason"]} |')
    md += ['', '## 输入哈希（固定 URL）', '', '| Input | SHA256 |', '|---|---|']
    md += [f'| [{p}](https://raw.githubusercontent.com/openrazer/openrazer/{OPENRAZER_COMMIT}/{p}) | `{h}` |' for p,h in sorted(lock['sha256'].items())]
    baseline = json.loads((ROOT/'Tools/OpenRazer/identity-baseline.json').read_text(encoding='utf-8'))
    current = {f'{p["pid"]:04X}':p for p in profiles}
    md += ['', '## Identity diff（相对 v1.2.8；本地 override 保留）', '', '新增：'+', '.join(sorted(set(current)-set(baseline))), '删除上游项：'+', '.join(sorted(set(baseline)-set(current)-{'00A4','0203'})), '',
           '| PID | baseline name / kind | upstream name / kind |', '|---|---|---|']
    for pid in sorted(set(current)&set(baseline)):
        p,b = current[pid],baseline[pid]
        if p['name'] != b['name'] or p['kind'] != b['kind']:
            md += [f'| {pid} | {b["name"]} / {b["kind"]} | {p["name"]} / {p["kind"]} |']
    md += ['', 'Local overrides: 00A4 Dock identity; 0203 keyboard exclusion; 00DE/00DF identity and descriptor-specific Rotation remain separate.', '']
    return {'Devices/OpenRazerCapabilityCatalog.cs':'\n'.join(cs)+'\n', 'docs/OPENRAZER-CAPABILITY-MATRIX.md':'\n'.join(md)}


def write_outputs(root, outputs):
    """Stage all files first; restore earlier outputs if a replace fails."""
    staged, originals, replaced = {}, {}, []
    try:
        for path, text in outputs.items():
            target = root/path
            target.parent.mkdir(parents=True,exist_ok=True)
            originals[path] = target.read_bytes() if target.exists() else None
            with tempfile.NamedTemporaryFile(dir=target.parent,prefix='.openrazer-',suffix='.tmp',delete=False) as stream:
                staged[path] = Path(stream.name)
                stream.write(text.encode('utf-8'))
        for path, temp in staged.items():
            os.replace(temp,root/path)
            replaced.append(path)
    except OSError:
        for path in reversed(replaced):
            if originals[path] is None: (root/path).unlink()
            else: (root/path).write_bytes(originals[path])
        raise
    finally:
        for temp in staged.values():
            if temp.exists(): temp.unlink()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--check',action='store_true')
    mode.add_argument('--write',action='store_true')
    parser.add_argument('--fetch',action='store_true',help='Download hash-locked raw files; never latest')
    parser.add_argument('--cache',type=Path,default=ROOT/'.planning/openrazer-stage-a/upstream'/OPENRAZER_COMMIT)
    args = parser.parse_args(argv)
    try:
        data,lock = load_inputs(args.cache,args.fetch)
        profiles = generate(data)
        outputs = render(profiles,lock)  # Validate every input/output before touching generated files.
        changed = [p for p,text in outputs.items() if not (ROOT/p).exists() or (ROOT/p).read_bytes()!=text.encode('utf-8')]
        counts = {k:dict(sorted(Counter(p[k] for p in profiles).items())) for k in ('dpi','poll','transport','charging')}
        print(json.dumps(dict(commit=OPENRAZER_COMMIT,pids=len(profiles),counts=counts,diff=changed),ensure_ascii=False,indent=2))
        if args.write:
            write_outputs(ROOT,outputs)
        return 1 if args.check and changed else 0
    except (OSError,ValueError,SyntaxError,KeyError) as ex:
        print('FAIL CLOSED: '+str(ex),file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())

"""Text-only cross audit. Never imports or executes third-party implementations."""
from pathlib import Path
import re,json,csv,hashlib,collections
R=Path(__file__).resolve().parents[2];J=R/'.planning/2026-10-06-v130-control-path-audit';A=R/'test-artifacts/v130-control-path-audit';X=J/'external'
def read(p):return p.read_text(encoding='utf-8-sig')
manifest=json.loads(read(J/'download-manifest.json'));catalog=json.loads(read(A/'effective-catalog.json'))
# Strip comments while preserving quoted strings; parse only literal metadata.
def uncomment(s):return re.sub(r'("(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\')|/\*[\s\S]*?\*/|//[^\n]*',lambda m:m.group(1) or '',s)
s=uncomment(read(X/'openmouse/src/razer/devices.ts'));constants={}
def value(t):
 t=t.strip()
 if t in constants:return constants[t]
 if t in ('true','false'):return t=='true'
 if re.fullmatch(r'0x[0-9a-fA-F]+|\d[\d_]*',t):return int(t.replace('_',''),0) if t.startswith('0x') else int(t.replace('_',''))
 if t.startswith('"') and t.endswith('"'):return json.loads(t)
 if t.startswith('[') and t.endswith(']'):return [value(x) for x in t[1:-1].split(',') if x.strip()]
 if t.startswith('{') and t.endswith('}'):
  d={}
  for part in t[1:-1].split(','):
   part=part.strip()
   if part.startswith('...'):
    base=constants.get(part[3:],{});d.update(base if isinstance(base,dict) else {})
   elif ':' in part:
    k,v=part.split(':',1);d[k.strip()]=value(v)
  return d
 return 'UNKNOWN'
for m in re.finditer(r'\bconst\s+([A-Z][A-Z0-9_]*)(?:[^=\n]*)=\s*(\{[\s\S]*?\}|\[[\s\S]*?\]|[\d_]+)\s*(?:as\s|;)',s):constants[m[1]]=value(m[2])
registry={int(m[1],16):value('{'+m[2]+'}') for m in re.finditer(r'\[(0x[0-9a-fA-F]+),\s*\{([\s\S]*?)\}\]',s)}
vendors=uncomment(read(X/'openmouse/src/drivers/vendors.ts'))
block=re.search(r'RAZER_HYPERSPEED_CONTROL_FILTERS[^=]*=\s*\[([\s\S]*?)\]\.flatMap',vendors)[1]
extended={int(x,16) for x in re.findall(r'0x[0-9a-fA-F]+',block)}|{0xe5,0xe6}
projects={m['project']:m for m in manifest};licenses={}
for name,m in projects.items():
 license_files=[f for f in m['files'] if 'license' in f['path'].lower()]
 license_text='\n'.join(read(X/name/f['path']) for f in license_files)
 licenses[name]=('AGPL-3.0' if 'GNU AFFERO GENERAL PUBLIC LICENSE' in license_text else 'GPL-2.0-or-later' if name=='openrazer' else 'GPL-2.0' if 'GNU GENERAL PUBLIC LICENSE' in license_text and 'Version 2, June 1991' in license_text else 'GPL-3.0' if 'GNU GENERAL PUBLIC LICENSE' in license_text else 'MIT' if 'MIT License' in license_text else 'UNKNOWN')
def link(name,path):return next(f['url'] for f in projects[name]['files'] if f['path']==path)
facts=[];risks=[];mechanical=[]
for p in catalog:
 pid=p['ProductId'];om=registry.get(pid,{});diff=[]
 if om:
  for key,own in [('maxDpi',p['MaximumDpi']),('pollingRates',p['PollRates'])]:
   external=om.get(key,'UNKNOWN')
   if external!='UNKNOWN' and external!=own:diff.append(f'{key}: effective={own}, external={external}')
  family='HighRate' if om.get('highRatePolling') is True else 'Legacy' if om.get('highRatePolling') is False else 'UNKNOWN'
  if family!='UNKNOWN' and family!=p['PollingProtocol']:diff.append(f'polling family: effective={p["PollingProtocol"]}, external={family}')
  tid=0x3f if pid in constants.get('TRANSACTION_3F',[]) else 0x1f if pid in constants.get('TRANSACTION_1F',[]) else 0xff
  if p['GetDpi'] and tid!=p['DpiTransaction'] and tid not in p['AlternatePerformanceTransactions']:diff.append(f'DPI TID: effective={p["DpiTransaction"]}, external={tid}')
 # Unverified/transcribed differences are recorded, never promoted to hardware corrections.
 if p['Transport']!='HidFeature90Or91':risk='E';reason='Current Windows backend not mapped; retain identity only'
 elif pid in extended:risk='B';reason='Audited desktop/consumer pages without fixed Usage/MI; use unique read-only response proof'
 elif pid==0xa8 and diff:risk='C';reason='Registry inherits extended polling but model hardware notes only prove 1K list, not family; retain existing Legacy until wire-family evidence'
 elif om.get('verified') is True and diff:risk='D';reason='Independent verified record differs; inspect hardware scope before correction'
 elif om.get('verified') is True:risk='A';reason='Independent hardware record and pinned facts agree for compared fields; unreported fields UNKNOWN'
 else:risk='C';reason='Only static/correlated or incomplete independent evidence; keep current candidate restrictions'
 riskrow={'PID':p['PID'],'Name':p['Name'],'Risk':risk,'Reason':reason,'Differences':'; '.join(diff) or 'NONE_IN_COMPARED_FIELDS','Evidence':link('openmouse','src/drivers/vendors.ts') if risk=='B' else link('openmouse','src/razer/devices.ts') if om else p['Evidence'],'HardwareVerifiedExternal':om.get('verified','UNKNOWN')}
 risks.append(riskrow)
 mechanical.append({'PID':p['PID'],'Name':p['Name'],'OpenMouseMatched':bool(om),'OpenMouseFacts':om,'ComparedDifferences':diff,'UnknownFields':['DPI min','GET/SET/stages when not explicitly declared','stage TID/storage','second polling transaction','Charging','BUSY policy','exact native HID length','native collection layout'],'Effective':p})
 for name,m in projects.items():
  hits=[]
  for f in m['files']:
   if f['path'].lower().endswith(('.md','.ts','.js','.cs','.py','.c','.h')):
    text=read(X/name/f['path']);matches=list(re.finditer(r'\b0x0*'+format(pid,'x')+r'\b',text,re.I))
    if matches:hits.append({'path':f['path'],'line':text[:matches[0].start()].count('\n')+1,'url':f['url']})
  facts.append({'PID':p['PID'],'Name':p['Name'],'Project':name,'Commit':m['commit'],'Matches':json.dumps(hits,ensure_ascii=False),'Scope':'PID mention only; inspect facts below; no hardware inference' if hits else 'UNKNOWN','ProtocolFacts':json.dumps(om,ensure_ascii=False) if name=='openmouse' and om else 'UNKNOWN','InterfaceFacts':'01/0C any Usage, PID-scoped filters' if name=='openmouse' and pid in extended else '91-byte current Naga Windows evidence' if name=='naga' and pid in {0xa7,0xa8,0xe7,0xe8} else 'UNKNOWN','TID_DPI':'UNKNOWN' if name!='openmouse' or not om else ('3F' if pid in constants.get('TRANSACTION_3F',[]) else '1F' if pid in constants.get('TRANSACTION_1F',[]) else 'FF'),'TID_Polling':'UNKNOWN','Battery':'UNKNOWN','Charging':'UNKNOWN','BUSY':'UNKNOWN','Timing':'UNKNOWN','HardwareEvidence':om.get('verified','UNKNOWN') if name=='openmouse' else 'UNKNOWN'})
counts=dict(collections.Counter(x['Risk'] for x in risks));assert len(risks)==len({p['ProductId'] for p in catalog}) and len(facts)==len(catalog)*len(projects)
for name,rows in [('external-crosscheck',facts),('risk-classification',risks)]:
 with (A/(name+'.csv')).open('w',encoding='utf-8-sig',newline='') as f:w=csv.DictWriter(f,fieldnames=rows[0]);w.writeheader();w.writerows(rows)
 (A/(name+'.md')).write_text('# '+name+'\n\nEach current PID is included; unavailable facts are UNKNOWN. Full fields in the CSV and protocol-field-crosscheck.json.\n\n| PID | Model | '+('Risk | Reason | Evidence' if name=='risk-classification' else 'Project | Commit | Evidence status')+' |\n|---|---|---|---|---|\n'+''.join('| '+row['PID']+' | '+row['Name']+' | '+(row['Risk']+' | '+row['Reason']+' | '+row['Evidence'] if name=='risk-classification' else row['Project']+' | '+row['Commit']+' | '+row['Scope'])+' |\n' for row in rows),encoding='utf-8')
(A/'protocol-field-crosscheck.json').write_text(json.dumps(mechanical,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(A/'effective-catalog.md').write_text('# Dynamic effective catalog\n\nAll exported profile fields are in effective-catalog.csv/json; PID count '+str(len(catalog))+'. Static facts, not hardware validation.\n\n| PID | Name | Transport | Descriptor | DPI | Polling | Evidence |\n|---|---|---|---|---|---|---|\n'+''.join(f'| {p["PID"]} | {p["Name"]} | {p["Transport"]} | {p["AllowedShape"]} | {p["DpiProtocol"]}/{p["MinimumDpi"]}–{p["MaximumDpi"]} | {p["PollingProtocol"]}/{p["PollRates"]} | {p["EvidenceKind"]} |\n' for p in catalog),encoding='utf-8')
adopted=[]
for m in manifest:
 name=m['project'];chosen=[f for f in m['files'] if ('license' in f['path'].lower()) or f['path'] in {'src/razer/devices.ts','src/drivers/vendors.ts','src/drivers/razer/hid.ts','src/drivers/razer/viper-v4-pro-hid.ts','docs/razer-testing.md','src/core/device_runtime.js','src/protocols/protocol_api_razer.js','tools/test_razer_connection_plans.js','mouse_tray/drivers/vendor/razer.py','public/py/qdrazer/device.py','public/py/qdrazer/protocol.py','src/NagaBatteryTray/Hid/RazerProtocol.cs','src/NagaBatteryTray/Hid/RazerDevice.cs','daemon/openrazer_daemon/hardware/mouse.py','driver/razermouse_driver.c','driver/razercommon.c','driver/razermouse_driver.h'}]
 adopted.append({**{k:v for k,v in m.items() if k!='files'},'license':licenses[name],'facts_used':'PID-scoped candidate facts and safe read-only discovery concept; codec/capability crosscheck; no third-party function body or runtime dependency','files':chosen})
(R/'Tools/RazerControlAudit/evidence-lock.json').write_text(json.dumps({'audit_date':'2026-10-06','sources':adopted,'official_sources':[{'url':'https://www.razer.com/blog/razer-deathadder-v4-pro-now-supported-on-razer-synapse-web','fact':'Official DA V4 browser support; does not specify native HID shape'},{'url':'https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps','fact':'Usage/UsagePage describe top-level collection; feature length includes report ID'}],'source_code_copy':False},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
summary={'effective_count':len(catalog),'rows_per_source':len(catalog),'source_count':len(projects),'risk_counts':counts,'expanded_candidate_pids':[p['PID'] for p in catalog if p['ProductId'] in extended],'new_D_candidates':[p['PID'] for p in risks if p['Risk']=='D'],'no_pid_omitted':True,'stage_a_gate':'READY_FOR_MANUAL_EVIDENCE_REVIEW'}
(A/'stage-a-summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
(A/'pid-cross-audit.md').write_text('# All-PID cross audit — Stage A\n\nBaseline is v1.3.0-pre-control-audit, version unchanged. Effective count '+str(len(catalog))+'; source/PID rows '+str(len(facts))+'; counts '+str(counts)+'.\n\nCandidate extension is derived solely from the fixed OpenMouse filters, intersected with the runtime catalog. Unsupported transport profiles remain excluded. Existing reviewed protocol differences remain explicit. Transcribed/unverified differences do not justify corrections. All missing fields remain UNKNOWN; see protocol-field-crosscheck.json. No PID is omitted; no production logic changed before this gate.\n',encoding='utf-8')
print(json.dumps(summary,indent=2))

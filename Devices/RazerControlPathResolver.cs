using System;
using System.Collections.Generic;
using System.Linq;

namespace RazerBatteryTray
{
    internal enum ControlProbeOutcome { Success, NoResponse, Unsupported, BusyTimeout, Malformed, TransportError }
    internal sealed class ControlPathPlan
    {
        internal string Key, PoolSignature;
        internal HidDescriptor[] Candidates, ProbePaths;
        internal bool Cached, WasConfirmed, Ambiguous;
    }
    internal sealed class ControlPathSession
    {
        internal string PoolSignature, Target;
        internal bool Confirmed, Ambiguous;
    }
    // Owns descriptor selection only. The caller supplies capability-specific
    // read-only proof; no packet construction or hardware writes live here.
    internal sealed class RazerControlPathResolver
    {
        private readonly Dictionary<string, ControlPathSession> sessions = new Dictionary<string, ControlPathSession>();
        internal static bool HasAuditedDesktopConsumerCandidates(int pid)
        {
            // Fixed OpenMouse ddcb173 filters, intersected with our catalog.
            // Evidence/commit/file hashes: Tools/RazerControlAudit/evidence-lock.json.
            switch(pid) {
                case 0x78: case 0x7A: case 0x7C: case 0x7D: case 0x9E: case 0x9F:
                case 0xAA: case 0xAB: case 0xB6: case 0xB7: case 0xB8: case 0xBE: case 0xBF:
                case 0xC2: case 0xC3: case 0xC4: case 0xC5: case 0xCC: case 0xCD:
                case 0xD6: case 0xD7: case 0xE5: case 0xE6: return true;
                default: return false;
            }
        }
        internal static string Signature(HidDescriptor d)
        {
            return d==null?null:d.VendorId+"|"+d.ProductId+"|"+d.InstanceKey+"|"+(d.Path??"").ToLowerInvariant()+"|"+
                d.Version+"|"+d.ReportLength+"|"+d.UsagePage+"|"+d.Usage;
        }
        internal void Clear() { sessions.Clear(); }
        internal bool IsLocked(HidDescriptor d)
        {
            ControlPathSession session;
            return d!=null && sessions.TryGetValue(d.InstanceKey,out session) && session.Confirmed && !session.Ambiguous && session.Target==Signature(d);
        }
        internal bool PoolUnchanged(HidDescriptor target,IEnumerable<HidDescriptor> descriptors)
        {
            ControlPathSession session;
            if(target==null || !sessions.TryGetValue(target.InstanceKey,out session) || !IsLocked(target)) return false;
            string pool=string.Join("\n",descriptors.Where(d=>d.InstanceKey==target.InstanceKey).Select(Signature).Distinct().OrderBy(s=>s,StringComparer.Ordinal));
            if(session.PoolSignature==pool) return true;
            sessions.Remove(target.InstanceKey); return false;
        }
        private static int Preference(DeviceCapabilityProfile p,HidDescriptor d)
        {
            if(RazerProtocolProfile.SupportsRotation(d)) return 3;
            if(p.RequiredInterfaceNumber>=0 && System.Text.RegularExpressions.Regex.IsMatch(d.Path??"",@"(?:^|[&#])mi_"+p.RequiredInterfaceNumber.ToString("x2")+@"(?=[&#]|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return 2;
            return d.CanProbe?1:0;
        }
        internal List<ControlPathPlan> Prepare(IEnumerable<HidDescriptor> descriptors)
        {
            var plans=new List<ControlPathPlan>();
            foreach(var group in descriptors.GroupBy(d=>d.InstanceKey)) {
                var p=DeviceCapabilityCatalog.Find(group.First().ProductId);
                if(p==null || p.Transport!=TransportKind.HidFeature90Or91) continue;
                var candidates=group.Where(p.AcceptsDescriptor).GroupBy(d=>(d.Path??"").ToLowerInvariant())
                    .Where(g=>g.Select(Signature).Distinct().Count()==1).Select(g=>g.First())
                    .OrderByDescending(d=>Preference(p,d)).ThenBy(d=>d.Path,StringComparer.OrdinalIgnoreCase).ToArray();
                if(candidates.Length==0) { sessions.Remove(group.Key); continue; }
                string pool=string.Join("\n",group.Select(Signature).Distinct().OrderBy(s=>s,StringComparer.Ordinal));
                ControlPathSession session;
                bool cached=sessions.TryGetValue(group.Key,out session) && session.PoolSignature==pool && candidates.Any(d=>Signature(d)==session.Target);
                if(!cached) sessions.Remove(group.Key);
                plans.Add(new ControlPathPlan { Key=group.Key, PoolSignature=pool, Candidates=candidates,
                    Cached=cached, WasConfirmed=cached && session.Confirmed, Ambiguous=cached && session.Ambiguous,
                    ProbePaths=cached?(session.Ambiguous?new HidDescriptor[0]:candidates.Where(d=>Signature(d)==session.Target).ToArray()):candidates });
            }
            var present=new HashSet<string>(plans.Select(p=>p.Key));
            foreach(string stale in sessions.Keys.Where(k=>!present.Contains(k)).ToArray()) sessions.Remove(stale);
            return plans;
        }
        internal bool Finish(ControlPathPlan plan, IEnumerable<HidDescriptor> successes)
        {
            var valid=successes.GroupBy(Signature).Select(g=>g.First()).ToArray();
            bool ambiguous=plan.Ambiguous || valid.Length>1;
            var sentinel=plan.ProbePaths.FirstOrDefault()??plan.Candidates[0];
            // An initially sleeping pool may have no previously proven path.
            // Poll one sentinel at a time rather than reprobe every sibling.
            if(valid.Length==0 && plan.Cached && !plan.WasConfirmed && !plan.Ambiguous) {
                int at=Array.FindIndex(plan.Candidates,d=>Signature(d)==Signature(sentinel));
                sentinel=plan.Candidates[(at+1)%plan.Candidates.Length];
            }
            sessions[plan.Key]=new ControlPathSession { PoolSignature=plan.PoolSignature,
                Target=Signature(valid.Length==1?valid[0]:sentinel),
                Confirmed=valid.Length==1 && !ambiguous, Ambiguous=ambiguous };
            return ambiguous;
        }
    }
}

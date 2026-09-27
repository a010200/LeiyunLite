using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RazerBatteryTray.Updates;
namespace RazerBatteryTray.Desktop
{
    internal sealed partial class ReleaseUpdateService
    {
        internal async Task<string> DownloadSigned(ReleaseOffer offer, InstallLayout layout, IProgress<int> progress, CancellationToken token)
        {
            if (offer == null || offer.SignedManifestUrl == null || offer.Version.Preview) throw new InvalidDataException("This release has no installable signed stable package.");
            string name = "LeiyunLite-" + offer.Tag + "-update.json";
            if (!AssetUrl(offer.SignedManifestUrl, offer.Tag, name)) throw new InvalidDataException("Invalid signed manifest location.");
            string envelope;
            using (var client = Client()) envelope = await ReadText(client, offer.SignedManifestUrl, true, 65536, token).ConfigureAwait(false);
            var m = UpdatePackage.Verify(envelope, UpdateTrust.PublicKey);
            if ("v" + m.Version != offer.Tag || Version.Parse(m.Version) <= Version.Parse(layout.Read().Current)) throw new InvalidDataException("Signed version mismatch/downgrade.");
            var signedOffer = new ReleaseOffer { Tag = offer.Tag, FileName = m.Package, Size = m.Size, Digest = m.Sha256,
                Url = "https://github.com/a010200/LeiyunLite/releases/download/" + offer.Tag + "/" + m.Package };
            string updates = Path.Combine(layout.Root, "updates"); InstallLayout.NoReparse(updates);
            string zip = await Download(signedOffer, progress, token, updates).ConfigureAwait(false);
            string job = Path.GetDirectoryName(zip);
            token.ThrowIfCancellationRequested();
            UpdatePackage.Extract(zip, Path.Combine(job, "verified"), m);
            InstallLayout.Atomic(Path.Combine(job, "update.json"), envelope);
            return job;
        }
    }
}

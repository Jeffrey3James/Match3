using System;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace MacFree.Editor
{
    /// ES256 JWTs for the App Store Connect API. Apple requires the JOSE
    /// (raw r||s) signature form, not ASN.1/DER - DerToJose converts.
    public static class AscJwt
    {
        static string cachedToken;
        static string cachedKey;
        static DateTime cachedExpiryUtc;

        /// Cache key covers everything CreateToken signs with: two settings
        /// objects (or the same one edited between calls, e.g. switching
        /// accounts) with different credentials must never share a token.
        public static string GetCached(MacFreeSettings s)
        {
            string pem = s.ResolveP8Pem();
            string key = s.AscKeyId + "|" + s.AscIssuerId + "|" + (pem ?? "").GetHashCode();
            DateTime now = DateTime.UtcNow;
            bool expiring = cachedToken == null || now >= cachedExpiryUtc.AddSeconds(-60);
            if (cachedToken != null && cachedKey == key && !expiring)
                return cachedToken;
            cachedToken = CreateToken(s.AscKeyId, s.AscIssuerId, pem, now);
            cachedKey = key;
            cachedExpiryUtc = now.AddMinutes(19);
            return cachedToken;
        }

        public static string CreateToken(string keyId, string issuerId, string p8Pem, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(p8Pem)) throw new InvalidOperationException("No .p8 key content.");
            long iat = (long)(nowUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            long exp = iat + 19 * 60;
            string header = "{\"alg\":\"ES256\",\"kid\":\"" + keyId + "\",\"typ\":\"JWT\"}";
            string payload = "{\"iss\":\"" + issuerId + "\",\"iat\":" + iat + ",\"exp\":" + exp
                + ",\"aud\":\"appstoreconnect-v1\"}";
            string signingInput = B64Url(Encoding.UTF8.GetBytes(header)) + "."
                + B64Url(Encoding.UTF8.GetBytes(payload));

            AsymmetricKeyParameter key;
            using (var reader = new System.IO.StringReader(p8Pem))
            {
                object o = new PemReader(reader).ReadObject();
                key = o as AsymmetricKeyParameter
                    ?? (o as AsymmetricCipherKeyPair)?.Private
                    ?? throw new InvalidOperationException("Could not parse the .p8 private key.");
            }

            var signer = SignerUtilities.GetSigner("SHA-256withECDSA");
            signer.Init(true, key);
            byte[] input = Encoding.UTF8.GetBytes(signingInput);
            signer.BlockUpdate(input, 0, input.Length);
            byte[] der = signer.GenerateSignature();
            return signingInput + "." + B64Url(DerToJose(der));
        }

        /// ASN.1 SEQUENCE{r INTEGER, s INTEGER} -> raw 64-byte r||s.
        public static byte[] DerToJose(byte[] der)
        {
            var seq = (Org.BouncyCastle.Asn1.Asn1Sequence)
                Org.BouncyCastle.Asn1.Asn1Object.FromByteArray(der);
            byte[] r = ((Org.BouncyCastle.Asn1.DerInteger)seq[0]).Value.ToByteArrayUnsigned();
            byte[] s = ((Org.BouncyCastle.Asn1.DerInteger)seq[1]).Value.ToByteArrayUnsigned();
            var jose = new byte[64];
            Array.Copy(r, 0, jose, 32 - r.Length, r.Length);
            Array.Copy(s, 0, jose, 64 - s.Length, s.Length);
            return jose;
        }

        static string B64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}

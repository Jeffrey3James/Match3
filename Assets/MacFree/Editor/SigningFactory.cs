using System.IO;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace MacFree.Editor
{
    /// Everything a Mac would normally do for iOS signing: an RSA-2048 key,
    /// a PKCS#10 CSR for Apple, and the final PKCS#12 bundle that Unity
    /// Build Automation consumes. Pure BouncyCastle - no openssl, no Mac.
    public static class SigningFactory
    {
        public static void CreateCsr(string commonName,
            out byte[] csrDer, out byte[] privateKeyPkcs8)
        {
            var gen = new RsaKeyPairGenerator();
            gen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();

            var subject = new X509Name("CN=" + commonName + ", OU=MacFree");
            var sigFactory = new Asn1SignatureFactory("SHA256WITHRSA", pair.Private);
            var csr = new Pkcs10CertificationRequest(
                sigFactory, subject, pair.Public, null);

            csrDer = csr.GetDerEncoded();
            privateKeyPkcs8 = PrivateKeyInfoFactory
                .CreatePrivateKeyInfo(pair.Private).GetDerEncoded();
        }

        public static byte[] CreateP12(byte[] certificateDer,
            byte[] privateKeyPkcs8, string password)
        {
            var cert = new X509CertificateParser().ReadCertificate(certificateDer);
            AsymmetricKeyParameter key = PrivateKeyFactory.CreateKey(privateKeyPkcs8);

            var store = new Pkcs12StoreBuilder().Build();
            var certEntry = new X509CertificateEntry(cert);
            store.SetCertificateEntry("macfree", certEntry);
            store.SetKeyEntry("macfree", new AsymmetricKeyEntry(key),
                new[] { certEntry });

            using (var ms = new MemoryStream())
            {
                store.Save(ms, password.ToCharArray(), new SecureRandom());
                return ms.ToArray();
            }
        }
    }
}

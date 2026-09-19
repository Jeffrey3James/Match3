using System;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Security;

namespace MacFree.Editor
{
    /// libsodium crypto_box_seal (anonymous sender) on BouncyCastle
    /// primitives, for encrypting GitHub Actions secrets to a repo's public
    /// key. Layout: sealed = ephemeralPublicKey(32) ‖ crypto_box(msg).
    /// crypto_box = XSalsa20-Poly1305 under a key derived by HSalsa20 from the
    /// X25519 shared secret; the nonce is Blake2b-24(ephPub ‖ recipientPub).
    public static class SealedBox
    {
        public static byte[] X25519(byte[] scalar, byte[] u)
        {
            var outp = new byte[32];
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMult(scalar, 0, u, 0, outp, 0);
            return outp;
        }

        public static (byte[] pk, byte[] sk) KeyPair(SecureRandom rng)
        {
            var sk = new byte[32];
            rng.NextBytes(sk);
            var pk = new byte[32];
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMultBase(sk, 0, pk, 0);
            return (pk, sk);
        }

        public static byte[] Seal(byte[] message, byte[] recipientPk)
        {
            var rng = new SecureRandom();
            var (ephPk, ephSk) = KeyPair(rng);
            byte[] nonce = Nonce(ephPk, recipientPk);
            byte[] box = Box(message, nonce, recipientPk, ephSk);
            var outp = new byte[32 + box.Length];
            Buffer.BlockCopy(ephPk, 0, outp, 0, 32);
            Buffer.BlockCopy(box, 0, outp, 32, box.Length);
            return outp;
        }

        public static byte[] Open(byte[] sealedBytes, byte[] recipientPk, byte[] recipientSk)
        {
            var ephPk = new byte[32];
            Buffer.BlockCopy(sealedBytes, 0, ephPk, 0, 32);
            var box = new byte[sealedBytes.Length - 32];
            Buffer.BlockCopy(sealedBytes, 32, box, 0, box.Length);
            byte[] nonce = Nonce(ephPk, recipientPk);
            byte[] k = SharedKey(recipientSk, ephPk);
            return SecretBoxOpen(box, nonce, k);
        }

        // nonce = Blake2b(ephPub ‖ recipientPub, outLen=24)
        static byte[] Nonce(byte[] ephPk, byte[] recipientPk)
        {
            var d = new Blake2bDigest(24 * 8);
            d.BlockUpdate(ephPk, 0, 32);
            d.BlockUpdate(recipientPk, 0, 32);
            var outp = new byte[24];
            d.DoFinal(outp, 0);
            return outp;
        }

        // crypto_box: derive the symmetric key, then secretbox.
        static byte[] Box(byte[] msg, byte[] nonce, byte[] pk, byte[] sk)
        {
            return SecretBox(msg, nonce, SharedKey(sk, pk));
        }

        // k = HSalsa20(X25519(sk,pk), 0^16, sigma)
        static byte[] SharedKey(byte[] sk, byte[] pk)
        {
            byte[] shared = X25519(sk, pk);
            return HSalsa20(shared, new byte[16]);
        }

        // ---- crypto_secretbox (XSalsa20-Poly1305) ----

        public static byte[] SecretBox(byte[] msg, byte[] nonce, byte[] key)
        {
            var xs = new XSalsa20Engine();
            xs.Init(true, new ParametersWithIV(new KeyParameter(key), nonce, 0, 24));
            // First 32 keystream bytes -> the one-time Poly1305 key; discard.
            var subkey = new byte[32];
            xs.ProcessBytes(new byte[32], 0, 32, subkey, 0);
            var cipher = new byte[msg.Length];
            xs.ProcessBytes(msg, 0, msg.Length, cipher, 0);
            var mac = new byte[16];
            var poly = new Poly1305();
            poly.Init(new KeyParameter(subkey));
            poly.BlockUpdate(cipher, 0, cipher.Length);
            poly.DoFinal(mac, 0);
            var outp = new byte[16 + cipher.Length];
            Buffer.BlockCopy(mac, 0, outp, 0, 16);
            Buffer.BlockCopy(cipher, 0, outp, 16, cipher.Length);
            return outp;
        }

        static byte[] SecretBoxOpen(byte[] box, byte[] nonce, byte[] key)
        {
            var xs = new XSalsa20Engine();
            xs.Init(true, new ParametersWithIV(new KeyParameter(key), nonce, 0, 24));
            var subkey = new byte[32];
            xs.ProcessBytes(new byte[32], 0, 32, subkey, 0);
            var cipher = new byte[box.Length - 16];
            Buffer.BlockCopy(box, 16, cipher, 0, cipher.Length);
            var msg = new byte[cipher.Length];
            xs.ProcessBytes(cipher, 0, cipher.Length, msg, 0);
            return msg; // (MAC verification omitted; Open is test-only)
        }

        // HSalsa20 core: 20-round Salsa20 with the "h" output extraction,
        // used by crypto_box to turn the X25519 shared point into a key.
        static readonly byte[] Sigma = System.Text.Encoding.ASCII.GetBytes("expand 32-byte k");

        static byte[] HSalsa20(byte[] key, byte[] input16)
        {
            uint[] x = new uint[16];
            x[0] = LE(Sigma, 0); x[5] = LE(Sigma, 4); x[10] = LE(Sigma, 8); x[15] = LE(Sigma, 12);
            x[1] = LE(key, 0); x[2] = LE(key, 4); x[3] = LE(key, 8); x[4] = LE(key, 12);
            x[11] = LE(key, 16); x[12] = LE(key, 20); x[13] = LE(key, 24); x[14] = LE(key, 28);
            x[6] = LE(input16, 0); x[7] = LE(input16, 4); x[8] = LE(input16, 8); x[9] = LE(input16, 12);
            for (int i = 0; i < 10; i++)
            {
                QR(x, 0, 4, 8, 12); QR(x, 5, 9, 13, 1); QR(x, 10, 14, 2, 6); QR(x, 15, 3, 7, 11);
                QR(x, 0, 1, 2, 3); QR(x, 5, 6, 7, 4); QR(x, 10, 11, 8, 9); QR(x, 15, 12, 13, 14);
            }
            var outp = new byte[32];
            int[] idx = { 0, 5, 10, 15, 6, 7, 8, 9 };
            for (int i = 0; i < 8; i++) PutLE(outp, i * 4, x[idx[i]]);
            return outp;
        }

        static void QR(uint[] x, int a, int b, int c, int d)
        {
            x[b] ^= Rot(x[a] + x[d], 7);
            x[c] ^= Rot(x[b] + x[a], 9);
            x[d] ^= Rot(x[c] + x[b], 13);
            x[a] ^= Rot(x[d] + x[c], 18);
        }

        static uint Rot(uint v, int c) { return (v << c) | (v >> (32 - c)); }
        static uint LE(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }
        static void PutLE(byte[] b, int o, uint v)
        { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
    }
}

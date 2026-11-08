using System.Security.Cryptography;
using Biz.Bizadm.KMS.Cipher;
using Biz.Bizadm.KMS.Credentials.Cipher;

namespace KspBridge.App.Broadcast
{
    /// <summary>
    /// Encrypts Broadcast.Speech.ApiKey in the config file with an OS credential store KEK and a local DEK
    /// (rule dotnet/kms-config-secrets, backend OsKms). Plain text is encrypted once and the YAML is rewritten.
    /// </summary>
    internal static class SecretBootstrap
    {
        /// <summary>Returns the plain API key for runtime use. Never logs the key.</summary>
        public static string UnprotectSpeechKey(AppConfiguration cfg, string configFilePath)
        {
            OsKmsOptions os = cfg.OsKms ?? throw new InvalidOperationException("OsKms is required when Broadcast.Speech.ApiKey is set");
            SpeechOptions speech = cfg.Broadcast!.Speech!;

            IOsKekCredentialStore store = OsKekCredentialProvider.CreateForCurrentOs(
                os.CredentialService, os.CredentialAccount, OsKekCredentialDefaults.Namespace);
            if (string.IsNullOrEmpty(os.Salt))
            {
                // First start only: KEK password and salt, before the DEK file is created.
                byte[] password = RandomNumberGenerator.GetBytes(32);
                try
                {
                    store.StorePassword(password);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(password);
                }
                os.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                // Record the salt before the DEK file exists (skill order): a later failure must not leave a DEK
                // wrapped with a salt that was never saved.
                Save(cfg, configFilePath);
            }

            using AesGcmKekManager manager = AesGcmKekManager.Create(store, Convert.FromBase64String(os.Salt), os.Iterations);
            FileInfo dekFile = new FileInfo(Path.Combine(cfg.ContentRootPath, os.SecretsDekFilePath));
            dekFile.Directory?.Create();
            using DekCipherService dek = new DekCipherService(AesGcmDekCipher.Create(manager.Current, dekFile));

            if (!dek.StartsWithFromPrefix(speech.ApiKey))
            {
                speech.ApiKey = dek.Encrypt(speech.ApiKey);
                Save(cfg, configFilePath);
            }
            return dek.Decrypt(speech.ApiKey);
        }

        /// <summary>Rewrites the config YAML atomically (temp file + replace). Comments are not kept.</summary>
        private static void Save(AppConfiguration cfg, string configFilePath)
        {
            YamlDotNet.Serialization.ISerializer serializer = new YamlDotNet.Serialization.SerializerBuilder()
                .ConfigureDefaultValuesHandling(YamlDotNet.Serialization.DefaultValuesHandling.OmitNull)
                .Build();
            string tmp = configFilePath + ".tmp";
            File.WriteAllText(tmp, serializer.Serialize(cfg));
            File.Move(tmp, configFilePath, overwrite: true);
        }
    }
}

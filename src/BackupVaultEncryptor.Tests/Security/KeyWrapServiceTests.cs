using System.Security.Cryptography;
using BackupVaultEncryptor.App.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupVaultEncryptor.Tests.Security
{
    [TestClass]
    public class KeyWrapServiceTests
    {
        [TestMethod]
        public void WrapAndUnwrapVaultMasterKey_WithPasswordDerivedKey_RoundTrips()
        {
            var service = new KeyWrapService();
            var keyToWrap = RandomNumberGenerator.GetBytes(32);
            var wrappingKey = RandomNumberGenerator.GetBytes(32);

            var wrapped = service.WrapWithKey(keyToWrap, wrappingKey);
            var unwrapped = service.UnwrapWithKey(wrapped, wrappingKey);

            CollectionAssert.AreEqual(keyToWrap, unwrapped);
        }
    }
}

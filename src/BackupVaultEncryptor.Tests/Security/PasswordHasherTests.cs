using BackupVaultEncryptor.App.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupVaultEncryptor.Tests.Security
{
    [TestClass]
    public class PasswordHasherTests
    {
        [TestMethod]
        public void HashAndVerify_Succeeds_ForCorrectPassword()
        {
            var hasher = new PasswordHasher();
            var password = "TestPassword!123";

            var hash = hasher.HashPassword(password);

            Assert.IsTrue(hasher.VerifyPassword(password, hash));
        }

        [TestMethod]
        public void HashAndVerify_Fails_ForWrongPassword()
        {
            var hasher = new PasswordHasher();
            var password = "TestPassword!123";
            var wrongPassword = "OtherPassword!456";

            var hash = hasher.HashPassword(password);

            Assert.IsFalse(hasher.VerifyPassword(wrongPassword, hash));
        }
    }
}

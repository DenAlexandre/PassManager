namespace PassManager.Core.Vault;

public class VaultFormatException(string message) : Exception(message);

public class VaultAuthenticationException(string message, Exception? inner = null) : Exception(message, inner);

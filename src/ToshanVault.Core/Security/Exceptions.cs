namespace ToshanVault.Core.Security;

public sealed class VaultLockedException : InvalidOperationException
{
    public VaultLockedException() : base("Vault is locked. Call UnlockAsync first.") { }
}

public sealed class VaultNotInitialisedException : InvalidOperationException
{
    public VaultNotInitialisedException() : base("Vault has not been initialised. Call InitialiseAsync first.") { }
}

public sealed class VaultAlreadyInitialisedException : InvalidOperationException
{
    public VaultAlreadyInitialisedException() : base("Vault is already initialised.") { }
}

public sealed class WrongPasswordException : Exception
{
    public WrongPasswordException() : base("Master password is incorrect.") { }
}

public sealed class TamperedDataException : Exception
{
    public TamperedDataException(string message) : base(message) { }
    public TamperedDataException(string message, Exception inner) : base(message, inner) { }
}

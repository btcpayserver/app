namespace BTCPayApp.Core.LDK;

public class LDKWalletLogger : LDKLogger
{
    public LDKWalletLogger(LDKWalletLoggerFactory ldkWalletLoggerFactory) : base(ldkWalletLoggerFactory)
    {
    }
}
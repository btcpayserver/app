using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayApp.Core.BTCPayServer
{
    public interface INfcService
    {
        event Action<string> TagDetected;

        void StartListening();
        void StopListening();
    }
}

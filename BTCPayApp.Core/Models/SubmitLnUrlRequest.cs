using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayApp.Core.Models
{
    public class SubmitLnUrlRequest
    {
        public string? Lnurl { get; set; }
        public string? InvoiceId { get; set; }
        public long? Amount { get; set; }
    }
}

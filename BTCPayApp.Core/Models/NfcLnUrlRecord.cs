using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayApp.Core.Models
{
    public class NfcLnUrlRecord
    {
        public byte[]? Payload { get; set; }

        //
        // Summary:
        //     LnUrl
        public string? LnUrl { get; set; }

        //
        // Summary:
        //     String formatted payload
        public string? Message { get; set; }

        //
        // Summary:
        //     Two letters ISO 639-1 Language Code (ex: en, fr, de...)
        public string? LanguageCode { get; set; }
    }
}

#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.Core.Models.Brochure
{
    public sealed class BrochureOfficeContact
    {
        public string Region { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Hours { get; set; } = string.Empty;
    }

    public sealed class BrochureContactConfig
    {
        public string OfficeAddress { get; set; } = "501 - 510 Burrard Street, Vancouver, BC V6C 3A8";

        public List<string> CoverContactLines { get; set; } = new()
        {
            "Suite 501 - 510 Burrard Street",
            "Vancouver, BC, V6C3A8",
            "Office: +1 604 685 9533",
            "contact@korstructural.com",
            "www.korstructural.com"
        };

        public List<BrochureOfficeContact> Offices { get; set; } = new()
        {
            new() { Region = "Vancouver", Contact = "John Markulin, M.Eng., P.Eng., Struct.Eng., PE, SE", Phone = "(604) 685-9533", Email = "contact@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
            new() { Region = "Vancouver Island", Contact = "Rory Beirne, M.Eng., P.Eng., Struct.Eng.", Phone = "(778) 652-1895", Email = "rbeirne@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
            new() { Region = "Okanagan", Contact = "Conor Murtagh, B.A.Sc., P.Eng.", Phone = "(778) 652-1887", Email = "cmurtagh@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" },
            new() { Region = "United States", Contact = "Jim DesRoches, BASc., P.Eng., PE", Phone = "(604) 999-7758", Email = "jdesroches@korstructural.com", Hours = "9AM to 5PM (Monday to Friday)" }
        };
    }
}

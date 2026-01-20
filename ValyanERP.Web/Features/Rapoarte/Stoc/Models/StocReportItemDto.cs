using System;

namespace ValyanERP.Web.Features.Rapoarte.Stoc.Models
{
    public class StocReportItemDto
    {
        public Guid ArticolId { get; set; }
        public string Cod { get; set; } = string.Empty;
        public string Denumire { get; set; } = string.Empty;
        public Guid? TipArticolId { get; set; }
        public Guid? PunctLucruId { get; set; }
        public decimal Cantitate { get; set; }
        public decimal Valoare { get; set; }
    }
}

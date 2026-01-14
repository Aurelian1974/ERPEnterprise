using System;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Models
{
    public class ItemType
    {
        public Guid Id { get; set; }
        public string ItemTypeCode { get; set; } = string.Empty;
        public string ItemTypeName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }
}
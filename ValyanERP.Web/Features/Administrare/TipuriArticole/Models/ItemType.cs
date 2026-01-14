using System;
using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Models
{
    public class ItemType
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Codul este obligatoriu")]
        [StringLength(50, ErrorMessage = "Codul nu poate depăși 50 caractere")]
        public string ItemTypeCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Denumirea este obligatorie")]
        [StringLength(200, ErrorMessage = "Denumirea nu poate depăși 200 caractere")]
        public string ItemTypeName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }
}
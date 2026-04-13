using System.ComponentModel.DataAnnotations;

namespace HanakaServer.Dtos
{
    public class SupportRequestDto
    {
        [Required]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = string.Empty;

        [StringLength(30)]
        public string? Phone { get; set; }

        [Required]
        [StringLength(120)]
        public string Topic { get; set; } = string.Empty;

        [Required]
        [StringLength(4000, MinimumLength = 10)]
        public string Message { get; set; } = string.Empty;
    }
}

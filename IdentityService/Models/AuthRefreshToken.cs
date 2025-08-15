namespace IdentityService.Models
{
    public class AuthRefreshToken
    {
        public int Id { get; set; }
        public required string Token { get; set; }
        public required string JwtId { get; set; }
        public DateTime CreationDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool Used { get; set; }
        public bool Invalidated { get; set; }
        public Guid UserId { get; set; }
    }
}

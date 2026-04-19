namespace HanakaServer.ViewModels.PickleballWeb
{
    public class PickleballWebAuthPageViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string BackHref { get; set; } = "/";
        public string BackLabel { get; set; } = "Quay lại";
        public string ReturnUrl { get; set; } = "/";
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public bool AgreedToTerms { get; set; }
    }
}

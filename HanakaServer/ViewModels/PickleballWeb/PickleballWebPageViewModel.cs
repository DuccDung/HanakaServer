namespace HanakaServer.ViewModels.PickleballWeb
{
    public class PickleballWebPageViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Eyebrow { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PageKind { get; set; } = string.Empty;
        public string BackHref { get; set; } = "/";
        public string BackLabel { get; set; } = "Trang chu";
        public string Icon { get; set; } = "apps-outline";
        public string SearchPlaceholder { get; set; } = "Tim kiem";
        public bool ShowSearch { get; set; } = true;
        public string ActiveTab { get; set; } = string.Empty;
    }

    public class PickleballWebDetailPageViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Eyebrow { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PageKind { get; set; } = string.Empty;
        public long EntityId { get; set; }
        public string BackHref { get; set; } = "/";
        public string BackLabel { get; set; } = "Quay lai";
        public string ActiveTab { get; set; } = string.Empty;
    }
}

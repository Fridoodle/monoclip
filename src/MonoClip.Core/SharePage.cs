using System.Net;
using System.Text;
namespace MonoClip.Core;

// The page a share link points to. Chat apps build a video preview from its Open Graph tags (like
// Medal or YouTube links) instead of probing the video file itself; browsers get a simple player.
// EmbedUrl: a smaller copy of the video for chat embeds (og:video); the page itself plays the original.
public sealed record SharePageInfo(string Title, string Description, string PageUrl, string VideoUrl, string ImageUrl, int Width, int Height, int ImageWidth, int ImageHeight, string? EmbedUrl = null, int EmbedWidth = 0, int EmbedHeight = 0);
public static class SharePage
{
    public static string Describe(Mp4Info? info) => info == null ? "Shared with MonoClip" : $"{(int)info.Duration.TotalMinutes}:{info.Duration.Seconds:00} · {info.Width}×{info.Height} · shared with MonoClip";
    public static string Build(SharePageInfo p)
    {
        string E(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"robots\" content=\"noindex\">");
        html.Append($"<title>{E(p.Title)}</title>");
        void Meta(string property, string value) => html.Append($"<meta property=\"{property}\" content=\"{E(value)}\">");
        Meta("og:site_name", "Clipped with MonoClip"); Meta("og:type", "video.other"); Meta("og:title", p.Title); Meta("og:description", p.Description); Meta("og:url", p.PageUrl);
        Meta("og:image", p.ImageUrl); Meta("og:image:secure_url", p.ImageUrl); Meta("og:image:type", "image/jpeg"); Meta("og:image:width", p.ImageWidth.ToString()); Meta("og:image:height", p.ImageHeight.ToString());
        var (embed, width, height) = p.EmbedUrl != null ? (p.EmbedUrl, p.EmbedWidth, p.EmbedHeight) : (p.VideoUrl, p.Width, p.Height);
        Meta("og:video", embed); Meta("og:video:secure_url", embed); Meta("og:video:type", "video/mp4"); Meta("og:video:width", width.ToString()); Meta("og:video:height", height.ToString());
        html.Append($"<meta name=\"twitter:card\" content=\"player\"><meta name=\"twitter:title\" content=\"{E(p.Title)}\"><meta name=\"twitter:image\" content=\"{E(p.ImageUrl)}\">");
        html.Append("<style>html,body{margin:0;height:100%;background:#0c0c0c;color:#f0f0f0;font:14px Segoe UI,system-ui,sans-serif}main{min-height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;padding:16px;box-sizing:border-box}video{max-width:100%;max-height:85vh;background:#000}p{margin:0;color:#a0a0a0}</style></head><body><main>");
        html.Append($"<video src=\"{E(p.VideoUrl)}\" poster=\"{E(p.ImageUrl)}\" controls autoplay muted playsinline></video><p>{E(p.Title)} · {E(p.Description)}</p>");
        html.Append("</main></body></html>");
        return html.ToString();
    }
}

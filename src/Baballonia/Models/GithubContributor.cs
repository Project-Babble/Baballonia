namespace Baballonia.Models;

public class GithubContributor
{
    public GithubContributor(string login, string htmlUrl, int contributions)
    {
        this.Login = login;
        HtmlUrl = htmlUrl;
        this.Contributions = contributions;
    }

    public string Login { get; set; }
    public string HtmlUrl { get; set; }
    public int Contributions { get; set; }
}

using System.Net;
using BugReportService.Models;

namespace BugReportService.Helpers;

public static class BugEmailBuilder
{
    public static string BuildPlainText(BugReport e) =>
        $@"Novo bug report
        ID: {e.Id}
        Severidade: {e.Severity}
        Status: {e.Status}
        Página: {e.PageUrl}
        E-mail: {e.ReporterEmail}
        Criado em: {e.CreatedAt:O}
        Descrição: {e.Description}
        Passos: {e.Steps}";

    public static string BuildHtml(BugReport e)
    {
        string esc(string? s) => WebUtility.HtmlEncode(s ?? "");
        return
            $@"<h3>Novo bug report</h3>
            <ul>
            <li><b>ID:</b> {e.Id}</li>
            <li><b>Severidade:</b> {esc(e.Severity.ToString())}</li>
            <li><b>Status:</b> {esc(e.Status.ToString())}</li>
            <li><b>Página:</b> {esc(e.PageUrl)}</li>
            <li><b>E-mail:</b> {esc(e.ReporterEmail)}</li>
            <li><b>Criado em:</b> {e.CreatedAt:O}</li>
            </ul>
            <h4>Descrição</h4>
            <p>{esc(e.Description)}</p>
            <h4>Passos</h4>
            <p>{esc(e.Steps)}</p>";
    }

    public static string SafeFileNameFromUrl(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? "screenshot" : name;
        }
        catch { return "screenshot"; }
    }
}

#nullable enable
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;

namespace Asic.Integrations.Shared;

/// <summary>
/// Oracle ADF, the framework behind every ASIC site Renewtron drives. A first visit returns a
/// "loopback" script holding a loop number and window id; the page is then requested again with
/// those and the browser's screen size, and only that second answer is the real page, carrying
/// the window id and ViewState every later post must send back.
/// </summary>
public static partial class Adf
{
    /// <summary>The event payload ADF sends with a button or link click (form-encoded).</summary>
    public const string ActionEvent =
        "%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22type%22%3E%3Cs%3Eaction%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E";

    /// <summary>The event payload ADF sends when an auto-submitting field (radio, select) changes (form-encoded).</summary>
    public const string ValueChangeEvent =
        "%3Cm+xmlns%3D%22http%3A%2F%2Foracle.com%2FrichClient%2Fcomm%22%3E%3Ck+v%3D%22autoSubmit%22%3E%3Cb%3E1%3C%2Fb%3E%3C%2Fk%3E%3Ck+v%3D%22suppressMessageShow%22%3E%3Cs%3Etrue%3C%2Fs%3E%3C%2Fk%3E%3Ck+v%3D%22type%22%3E%3Cs%3EvalueChange%3C%2Fs%3E%3C%2Fk%3E%3C%2Fm%3E";

    /// <summary>The loop number in the loopback script, e.g. <c>'_afrLoop', '4242'</c>.</summary>
    public static string? LoopId(string? html) => Group(LoopRe(), html);

    /// <summary>The window id in the loopback script, e.g. <c>'_afrPage', '', 'w0'</c>.</summary>
    public static string? ScriptWindowId(string? html) => Group(WindowRe(), html);

    /// <summary>
    /// The query string a browser adds when it answers the loopback: its window mode and screen
    /// metrics. Sites were captured with slightly different window heights; each client keeps its own.
    /// </summary>
    public static string LoopbackQuery(string? loop, string? windowId, int windowMode = 2, int windowHeight = 926) =>
        $"_afrLoop={loop}&_afrWindowMode={windowMode}&Adf-Window-Id={windowId}" +
        $"&_afrFS=16&_afrMT=screen&_afrMFW=1865&_afrMFH={windowHeight}&_afrMFDW=1920&_afrMFDH=1080&_afrMFC=8&_afrMFCI=0&_afrMFM=0&_afrMFR=96&_afrMFG=0&_afrMFS=0&_afrMFO=0";

    /// <summary>The page's own window id and ViewState, from its hidden inputs.</summary>
    public static (string? WindowId, string? ViewState) HiddenFields(IHtmlDocument document) =>
        (document.QuerySelector("input[name='Adf-Window-Id']")?.GetAttribute("value"),
         document.QuerySelector("input[name='javax.faces.ViewState']")?.GetAttribute("value"));

    private static string? Group(Regex re, string? html) =>
        html is not null && re.Match(html) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"'?_afrLoop',\s*'(\d+)'")] private static partial Regex LoopRe();
    [GeneratedRegex(@"'?_afrPage'\s*,\s*''\s*,\s*'([^']+)'")] private static partial Regex WindowRe();
}

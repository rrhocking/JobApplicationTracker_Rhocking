using System.Diagnostics;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

/// <summary>
/// Shows friendly pages for failures. Two things send visitors here:
/// UseExceptionHandler("/error") after an unhandled exception, and
/// UseStatusCodePagesWithReExecute("/error/{0}") for empty 4xx/5xx responses such as a 404.
/// </summary>
[Route("error")]
public class ErrorController : Controller
{
    [Route("")]
    [Route("{statusCode:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index(int? statusCode)
    {
        // An unhandled exception arrives at plain /error with no code, so treat it as a 500.
        // Anything outside 400-599 is not an error status, so it is also shown as a 500.
        var code = statusCode is >= 400 and <= 599
            ? statusCode.Value
            : StatusCodes.Status500InternalServerError;

        // Keep the real status code on the response so browsers, search engines and logs see it.
        Response.StatusCode = code;

        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View(ErrorViewModel.For(code, requestId));
    }
}

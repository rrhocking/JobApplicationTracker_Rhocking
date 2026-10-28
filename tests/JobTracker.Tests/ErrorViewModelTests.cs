using JobTracker.Web.Models;

namespace JobTracker.Tests;

public class ErrorViewModelTests
{
    [Fact]
    public void NotFound_gets_a_friendly_404_message()
    {
        var model = ErrorViewModel.For(404, "abc");

        Assert.Equal(404, model.StatusCode);
        Assert.Equal("Page not found", model.Title);
        Assert.Equal("abc", model.RequestId);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public void Server_errors_get_the_generic_something_went_wrong_message(int code)
    {
        var model = ErrorViewModel.For(code, "id");

        Assert.Equal(code, model.StatusCode);
        Assert.Equal("Something went wrong", model.Title);
    }

    [Fact]
    public void Other_client_errors_fall_back_to_a_generic_message()
    {
        var model = ErrorViewModel.For(403, "id");

        Assert.Equal(403, model.StatusCode);
        Assert.Equal("We couldn't complete that request", model.Title);
    }

    [Fact]
    public void Messages_never_contain_exception_details()
    {
        var model = ErrorViewModel.For(500, "id");

        Assert.DoesNotContain("Exception", model.Message);
        Assert.DoesNotContain("at JobTracker", model.Message);
    }
}

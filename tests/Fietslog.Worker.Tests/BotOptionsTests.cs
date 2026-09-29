using System.ComponentModel.DataAnnotations;
using Fietslog.Worker;

namespace Fietslog.Worker.Tests;

public class BotOptionsTests
{
    [Theory]
    [InlineData("123456:ABC-def_ghi", true)]
    [InlineData("7123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw", true)]
    [InlineData("", false)]
    [InlineData("ABC-def_ghi", false)]
    [InlineData("123456", false)]
    [InlineData("123456:", false)]
    [InlineData("abc:def", false)]
    [InlineData("123456:ABC def", false)]
    [InlineData(" 123456:ABC", false)]
    public void Validates_token_format(string token, bool valid)
    {
        var options = new BotOptions { Token = token, AllowedUserId = 1 };

        Assert.Equal(valid, Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true));
    }
}

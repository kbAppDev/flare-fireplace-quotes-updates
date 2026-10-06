namespace FlareQuotes.Core.Messaging;

public static class PhoneLinkMessageHelper
{
    // Microsoft's Windows Settings URI list documents this entry as opening Phone Link.
    // It launches the app only; recipients and messages stay under the user's control.
    public const string LaunchUri = "ms-settings:mobile-devices-addphone-direct";

    public static bool TryNormalizePhone(string? value, out string normalizedPhone)
    {
        normalizedPhone = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var input = value.Trim();
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (character is >= '0' and <= '9' || character == '+' && index == 0)
                result.Append(character);
            else if (!char.IsWhiteSpace(character) && character is not ('(' or ')' or '-' or '.'))
                return false;
        }

        var digitCount = result.ToString().Count(character => character is >= '0' and <= '9');
        if (digitCount is < 7 or > 15)
            return false;

        normalizedPhone = result.ToString();
        return true;
    }
}

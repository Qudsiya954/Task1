// See https://aka.ms/new-console-template for more information
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

var settings = Settings.FromArgs(args);

var chromeOptions = new ChromeOptions();
chromeOptions.AddArgument("--start-maximized");

using var driver = new ChromeDriver(chromeOptions);
var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(12));

try
{
    driver.Navigate().GoToUrl(settings.Url);

    var firstName = SemanticFinder.FindField(driver, wait, "First Name", "text");
    firstName.Clear();
    firstName.SendKeys(settings.FirstName);

    SemanticFinder.SelectRadio(driver, wait, "Gender", settings.Gender);

    var stateDropdown = SemanticFinder.FindField(driver, wait, "State", "select");
    var stateSelect = new SelectElement(stateDropdown);

    var requestedState = stateSelect.Options.FirstOrDefault(option =>
        option.Text.Trim().Equals(settings.State, StringComparison.OrdinalIgnoreCase));

    if (requestedState != null)
    {
        requestedState.Click();
    }
    else
    {
        var firstRealState = stateSelect.Options.FirstOrDefault(option =>
            option.Enabled &&
            !string.IsNullOrWhiteSpace(option.Text) &&
            !option.Text.Contains("select", StringComparison.OrdinalIgnoreCase));

        if (firstRealState == null)
            throw new NoSuchElementException("The State dropdown has no selectable options.");

        Console.WriteLine(
            $"Requested state '{settings.State}' was unavailable. " +
            $"Selected '{firstRealState.Text}' instead.");

        firstRealState.Click();
    }

    Console.WriteLine("PASS: First Name, Gender, and State were filled successfully.");
    Console.WriteLine($"First Name: {firstName.GetAttribute("value")}");
    Console.WriteLine($"Gender: {settings.Gender}");
    Console.WriteLine($"State: {stateSelect.SelectedOption.Text}");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    var screenshotDriver = (ITakesScreenshot)driver;
    screenshotDriver.GetScreenshot().SaveAsFile("failure.png");
    Environment.ExitCode = 1;
}
finally
{
    Console.WriteLine("Press Enter to close the browser...");
    Console.ReadLine();
}

public record Settings(string Url, string FirstName, string Gender, string State)
{
    public static Settings FromArgs(string[] args)
    {
        string Get(string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index < args.Length - 1 ? args[index + 1] : fallback;
        }

        return new Settings(
            Get("--url", Environment.GetEnvironmentVariable("TEST_URL")
                         ?? "https://app.cloudqa.io/home/AutomationPracticeForm"),
            Get("--first-name", "Qudsiya"),
            Get("--gender", "Female"),
            Get("--state", "Uttar Pradesh")
        );
    }
}

public static class SemanticFinder
{
    public static IWebElement FindField(
        IWebDriver driver,
        WebDriverWait wait,
        string visibleLabel,
        string expectedKind)
    {
        return wait.Until(d =>
        {
            // 1. Find visible human-readable label/heading, not a hard-coded ID/class.
            var label = FindVisibleText(d, visibleLabel);

            // 2. Use HTML label-for relationship if present.
            var targetId = label?.GetAttribute("for");
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                var linked = d.FindElements(By.Id(targetId)).FirstOrDefault();
                if (linked != null && IsExpected(linked, expectedKind))
                    return linked;
            }

            // 3. Use a field contained inside the label or its nearby semantic container.
            if (label != null)
            {
                var contained = label.FindElements(By.CssSelector("input, textarea, select"))
                    .FirstOrDefault(x => IsExpected(x, expectedKind));

                if (contained != null)
                    return contained;

                // 4. Spatial fallback: choose the closest suitable visible control
                // beside/below the label. This tolerates modest DOM restructuring.
                var nearby = FindNearestControl(d, label, expectedKind);
                if (nearby != null)
                    return nearby;
            }

            // 5. Accessibility fallback: aria-label / placeholder / name can identify it.
            return FindByAccessibleName(d, visibleLabel, expectedKind);
        }) ?? throw new NoSuchElementException(
            $"Could not find a {expectedKind} associated with '{visibleLabel}'.");
    }

    public static void SelectRadio(
        IWebDriver driver,
        WebDriverWait wait,
        string groupLabel,
        string optionText)
    {
        wait.Until(d =>
        {
            // Find option by its visible text, then use its linked radio control.
            var optionLabel = FindVisibleText(d, optionText);

            if (optionLabel != null)
            {
                var targetId = optionLabel.GetAttribute("for");
                if (!string.IsNullOrWhiteSpace(targetId))
                {
                    var linked = d.FindElements(By.Id(targetId))
                        .FirstOrDefault(x => x.GetAttribute("type") == "radio");

                    if (linked != null)
                    {
                        ClickSafely(d, linked);
                        return true;
                    }
                }

                var containedRadio = optionLabel.FindElements(By.CssSelector("input[type='radio']"))
                    .FirstOrDefault();

                if (containedRadio != null)
                {
                    ClickSafely(d, containedRadio);
                    return true;
                }
            }

            // Fallback: find the Gender heading and select the closest radio.
            var group = FindVisibleText(d, groupLabel);
            if (group != null)
            {
                var radio = FindNearestControl(d, group, "radio");
                if (radio != null)
                {
                    ClickSafely(d, radio);
                    return true;
                }
            }

            return false;
        });
    }

    private static IWebElement? FindVisibleText(IWebDriver driver, string text)
    {
        string xpath =
            $"//*[normalize-space(.)={XPathLiteral(text)} and " +
            "not(self::script) and not(self::style)]";

        return driver.FindElements(By.XPath(xpath))
            .Where(e => e.Displayed)
            .OrderBy(e => e.Text.Length)
            .FirstOrDefault();
    }

    private static IWebElement? FindByAccessibleName(
        IWebDriver driver,
        string text,
        string expectedKind)
    {
        return driver.FindElements(By.CssSelector("input, textarea, select"))
            .Where(e => e.Displayed && IsExpected(e, expectedKind))
            .FirstOrDefault(e =>
            {
                string description = string.Join(" ",
                    e.GetAttribute("aria-label"),
                    e.GetAttribute("placeholder"),
                    e.GetAttribute("name"),
                    e.GetAttribute("id"));

                return description.Contains(text, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static IWebElement? FindNearestControl(
        IWebDriver driver,
        IWebElement label,
        string expectedKind)
    {
        var script = (IJavaScriptExecutor)driver;

        return script.ExecuteScript(@"
            const label = arguments[0];
            const kind = arguments[1];
            const labelBox = label.getBoundingClientRect();

            const candidates = [...document.querySelectorAll('input, textarea, select')]
              .filter(e => {
                if (!e.offsetParent || e.disabled) return false;
                if (kind === 'select') return e.tagName === 'SELECT';
                if (kind === 'radio') return e.type === 'radio';
                return (e.tagName === 'INPUT' || e.tagName === 'TEXTAREA') &&
                       e.type !== 'radio' && e.type !== 'checkbox';
              });

            let best = null;
            let bestScore = Infinity;

            for (const candidate of candidates) {
                const box = candidate.getBoundingClientRect();
                const horizontalGap = Math.abs(box.left - labelBox.right);
                const verticalGap = Math.abs(box.top - labelBox.bottom);

                // Prefer controls to the right or immediately below a label.
                const isNearby =
                    (box.left >= labelBox.left - 20 && box.top <= labelBox.bottom + 180) ||
                    (box.top >= labelBox.top - 20 && box.top <= labelBox.bottom + 180);

                if (!isNearby) continue;

                const score = horizontalGap + verticalGap;
                if (score < bestScore) {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        ", label, expectedKind) as IWebElement;
    }

    private static bool IsExpected(IWebElement element, string expectedKind)
    {
        if (!element.Displayed) return false;

        return expectedKind switch
        {
            "select" => element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase),
            "radio" => element.GetAttribute("type") == "radio",
            "text" => element.TagName.Equals("textarea", StringComparison.OrdinalIgnoreCase)
                      || (element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase)
                          && element.GetAttribute("type") is not "radio" and not "checkbox"),
            _ => false
        };
    }

    private static void ClickSafely(IWebDriver driver, IWebElement element)
    {
        ((IJavaScriptExecutor)driver)
            .ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", element);

        element.Click();
    }

    private static string XPathLiteral(string value)
    {
        if (!value.Contains("'")) return $"'{value}'";
        if (!value.Contains("\"")) return $"\"{value}\"";

        var parts = value.Split('\'');
        return "concat(" + string.Join(", \"'\", ",
            parts.Select(part => $"'{part}'")) + ")";
    }
}
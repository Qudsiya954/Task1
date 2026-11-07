# Resilient Form Automation with C# and Selenium

## Overview

This project automates three fields on a web form:

- First Name text box
- Gender radio button
- State dropdown

The target page URL and test data are configurable through command-line arguments.

## Technology

- C#
- .NET 8
- Selenium WebDriver
- Google Chrome

## Setup

Install the .NET 8 SDK and Google Chrome.

Then, open Command Prompt in this project folder and run:

```cmd
dotnet restore
```

## Run

```cmd
dotnet run -- --url "https://app.cloudqa.io/home/AutomationPracticeForm" --first-name "Qudsiya" --gender "Female" --state "Uttar Pradesh"
```

Approach
The program does not depend on a fixed element ID, CSS class, or fixed DOM XPath.

It locates elements using visible labels and several fallback strategies:
1. HTML label-to-control relationship (for attribute)
2. Controls inside the label or nearby semantic container
3. Visual proximity between a label and a suitable control
4. Accessibility attributes such as aria-label, placeholder, name, and ID
For the State dropdown, if the requested value is unavailable, the program selects the first available real option and reports this in the console.
Limitations
No automation locator can survive every possible redesign. This solution expects the form’s meaningful visible labels or accessible metadata to remain available. A redesign that removes or renames these meanings may require updates to the test intent or locator strategy.

On failure, the program saves a screenshot as failure.png.
using System.Windows.Automation;
using System.Runtime.InteropServices;

namespace WorkflowRecorder.Core;

public static class UiAutomationInspector
{
    public static UiControlInfo? FromPoint(int x, int y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element is null)
            {
                return null;
            }

            var current = element.Current;
            var rect = current.BoundingRectangle;
            return new UiControlInfo
            {
                Name = Trim(current.Name, 300),
                ControlType = Trim(current.LocalizedControlType, 100),
                AutomationId = Trim(current.AutomationId, 200),
                ClassName = Trim(current.ClassName, 200),
                FrameworkId = Trim(current.FrameworkId, 100),
                IsPassword = current.IsPassword,
                Bounds = rect.IsEmpty
                    ? null
                    : new RectInfo
                    {
                        Left = (int)Math.Round(rect.Left),
                        Top = (int)Math.Round(rect.Top),
                        Width = (int)Math.Round(rect.Width),
                        Height = (int)Math.Round(rect.Height)
                    }
            };
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned[..Math.Min(max, cleaned.Length)];
    }
}

using Microsoft.Playwright;

namespace Playwright.NSE.Indices.Browser;

public static class PageInteractions
{
    /// <summary>
    /// Selects a dropdown option by visible text.
    /// If specificSelector is provided, only that dropdown is searched.
    /// Otherwise, all visible dropdowns on the page are scanned.
    /// Returns true if the option was found and selected.
    /// </summary>
    public static async Task<bool> SelectDropdownByTextAsync(
        IPage page,
        string optionText,
        string? specificSelector = null)
    {
        return await page.EvaluateAsync<bool>(@"(args) => {
            const { text, specificSel } = args;
            const normalize = v => (v || '').replace(/\s+/g, ' ').trim();

            const selects = specificSel
                ? Array.from(document.querySelectorAll(specificSel))
                : Array.from(document.querySelectorAll('select'));

            for (const select of selects) {
                // Skip hidden dropdowns
                if (select.offsetParent === null) continue;

                const option = Array.from(select.options)
                    .find(o => normalize(o.textContent) === normalize(text));

                if (option) {
                    select.value = option.value;

                    if (typeof jQuery !== 'undefined') {
                        jQuery(select).val(option.value).trigger('change');
                    } else {
                        select.dispatchEvent(new Event('input',  { bubbles: true }));
                        select.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                    return true;
                }
            }
            return false;
        }", new { text = optionText, specificSel = specificSelector });
    }

    /// <summary>
    /// Sets a date on a jQuery UI datepicker input.
    /// Falls back to native input events if jQuery is unavailable.
    /// Value format: dd-MM-yyyy
    /// </summary>
    public static async Task SetDateAsync(IPage page, string selector, string value)
    {
        var parts = value.Split('-');
        var day   = parts[0];
        var month = parts[1];
        var year  = parts[2];

        await page.EvaluateAsync($@"() => {{
            const sel = '{selector}';
            const id  = sel.replace('#', '');

            if (typeof jQuery !== 'undefined' && jQuery('#' + id).datepicker) {{
                try {{
                    jQuery('#' + id).datepicker('setDate', new Date({year}, {month} - 1, {day}));
                    return;
                }} catch(e) {{}}
            }}

            const el = document.querySelector(sel);
            if (!el) return;

            const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            setter.call(el, '{value}');
            el.dispatchEvent(new Event('input',  {{ bubbles: true }}));
            el.dispatchEvent(new Event('change', {{ bubbles: true }}));
            el.dispatchEvent(new Event('blur',   {{ bubbles: true }}));
        }}");
    }

    /// <summary>
    /// Clicks an element via JavaScript, bypassing Playwright's visibility
    /// and viewport requirements.
    /// </summary>
    public static async Task JsClickAsync(IPage page, string selector)
    {
        await page.EvaluateAsync($@"() => {{
            const el = document.querySelector('{selector}');
            if (el) el.click();
        }}");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens.Flight.Dialogs;

namespace KspBridge.Handlers
{
    /// <summary>Game popups: list, press a button, close the experiment results dialog.</summary>
    public static class DialogHandler
    {
        /// <summary>GET /api/dialogs</summary>
        public static object List()
        {
            List<object> popups = new List<object>();
            List<PopupDialog> open = Open();
            for (int i = 0; i < open.Count; i++)
            {
                MultiOptionDialog d = open[i].dialogToDisplay;
                popups.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["name"] = d?.name,
                    ["title"] = d?.title,
                    ["message"] = d?.message,
                    ["buttons"] = Buttons(d).Select(b => (object)b.OptionText).ToList(),
                });
            }

            Dictionary<string, object> results = null;
            ExperimentsResultDialog erd = ExperimentsResultDialog.Instance;
            if (erd != null)
            {
                results = new Dictionary<string, object>
                {
                    ["pages"] = erd.pages.Select(p => (object)new Dictionary<string, object>
                    {
                        ["title"] = p.title,
                        ["result"] = p.resultText,
                        ["science_value"] = (double)p.scienceValue,
                        ["value_after_recovery"] = (double)p.valueAfterRecovery,
                        ["value_after_transmit"] = (double)p.valueAfterTransmit,
                    }).ToList(),
                };
            }

            return new Dictionary<string, object> { ["popups"] = popups, ["experiment_results"] = results };
        }

        /// <summary>POST /api/dialogs/click {dialog: name|index, button: text|index}</summary>
        public static object Click(RequestBody body)
        {
            List<PopupDialog> open = Open();
            if (open.Count == 0)
            {
                throw ApiException.NotFound("no popup dialog is open");
            }

            PopupDialog popup = Pick(open, body, "dialog", p => p.dialogToDisplay?.name, p => p.dialogToDisplay?.title);
            List<DialogGUIButton> buttons = Buttons(popup.dialogToDisplay);
            DialogGUIButton button = Pick(buttons, body, "button", b => b.OptionText, b => b.OptionText);

            if (button.OptionInteractableCondition != null && !button.OptionInteractableCondition())
            {
                throw ApiException.BadRequest("button '" + button.OptionText + "' is disabled");
            }
            string text = button.OptionText;
            button.OptionSelected();
            if (button.DismissOnSelect && popup != null)
            {
                popup.Dismiss();
            }
            return new Dictionary<string, object> { ["clicked"] = text, ["dialog"] = popup.dialogToDisplay?.name };
        }

        /// <summary>POST /api/dialogs/close-experiment</summary>
        public static object CloseExperiment()
        {
            ExperimentsResultDialog erd = ExperimentsResultDialog.Instance;
            if (erd == null)
            {
                throw ApiException.NotFound("experiment results dialog is not open");
            }
            erd.Dismiss();
            return new Dictionary<string, object> { ["closed"] = true, ["note"] = "data stays in the parts" };
        }

        private static List<PopupDialog> Open()
        {
            return UnityEngine.Object.FindObjectsOfType<PopupDialog>()
                .Where(p => p != null && p.gameObject.activeInHierarchy && p.dialogToDisplay != null)
                .ToList();
        }

        private static List<DialogGUIButton> Buttons(MultiOptionDialog d)
        {
            List<DialogGUIButton> list = new List<DialogGUIButton>();
            if (d?.Options != null)
            {
                foreach (DialogGUIBase o in d.Options)
                {
                    Collect(o, list);
                }
            }
            return list;
        }

        private static void Collect(DialogGUIBase node, List<DialogGUIButton> list)
        {
            if (node == null)
            {
                return;
            }
            if (node is DialogGUIButton b)
            {
                list.Add(b);
            }
            if (node.children != null)
            {
                foreach (DialogGUIBase c in node.children)
                {
                    Collect(c, list);
                }
            }
        }

        /// <summary>Picks by integer index or by case-insensitive exact/substring match on the given texts.</summary>
        private static T Pick<T>(List<T> items, RequestBody body, string key, Func<T, string> name, Func<T, string> title)
        {
            if (!body.Has(key))
            {
                if (items.Count == 1 && key == "dialog")
                {
                    return items[0];
                }
                throw ApiException.BadRequest("'" + key + "' is required (name/text or index)");
            }
            object raw = body.GetRaw(key);
            if (raw is double d)
            {
                int i = (int)d;
                if (i < 0 || i >= items.Count)
                {
                    throw ApiException.NotFound(key + " index " + i + " out of range (count " + items.Count + ")");
                }
                return items[i];
            }
            string s = raw as string ?? throw ApiException.BadRequest("'" + key + "' must be a string or index");
            T exact = items.FirstOrDefault(x => string.Equals(name(x), s, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(title(x), s, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
            List<T> partial = items.Where(x => (name(x) ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                             || (title(x) ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (partial.Count == 1)
            {
                return partial[0];
            }
            throw ApiException.NotFound(partial.Count == 0 ? "no " + key + " matches '" + s + "'" : "'" + s + "' matches " + partial.Count + " " + key + "s, be more specific");
        }
    }
}

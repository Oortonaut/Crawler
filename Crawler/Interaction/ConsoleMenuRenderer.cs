namespace Crawler;

/// <summary>
/// Console-based menu renderer that maintains current text-based UI behavior.
/// Generates linear menu from MenuContext/InteractionContext and processes text input.
/// </summary>
public class ConsoleMenuRenderer : IMenuRenderer {
    /// <summary>
    /// Render complete MenuContext with all action types.
    /// </summary>
    public MenuSelection Render(MenuContext context, string title = "", string defaultOption = "") {
        if (title != "") {
            Console.Write(Style.MenuTitle.Format($"{title}"));
        }
        Console.Write(Style.MenuNormal.StyleString());

        // Process immediate interactions first
        foreach (var group in context.InteractionGroups) {
            foreach (var interaction in group.RawInteractions) {
                var msg = interaction.MessageFor(context.Agent);
                if (!string.IsNullOrEmpty(msg)) {
                    context.Agent.Message(Style.Em.Format(msg));
                }
                if (interaction.GetImmediacy() == Immediacy.Immediate) {
                    var ap = interaction.Perform();
                    return new MenuSelection {
                        SelectedInteraction = interaction,
                        Arguments = ""
                    };
                }
            }
        }

        // Build flat menu items from all action categories
        var menuItems = new List<MenuItem>();

        // System actions
        foreach (var action in context.SystemActions.Where(a => a.IsVisible)) {
            menuItems.Add(new ActionMenuItem(
                action.OptionCode,
                action.Description,
                args => action.Perform(args),
                action.IsEnabled ? EnableArg.Enabled : EnableArg.Disabled,
                ShowArg.Show
            ));
        }

        // Player actions (visible ones - submenu headers)
        if (context.PlayerActions.Any()) {
            menuItems.Add(MenuItem.Sep);
            menuItems.Add(new MenuItem("", Style.MenuTitle.Format("Player Menu")));
            foreach (var action in context.PlayerActions.Where(a => a.IsVisible)) {
                menuItems.Add(new ActionMenuItem(
                    action.OptionCode,
                    action.Description,
                    args => action.Perform(args),
                    action.IsEnabled ? EnableArg.Enabled : EnableArg.Disabled,
                    ShowArg.Show
                ));
            }
        }

        // Interaction groups
        if (context.InteractionGroups.Any()) {
            menuItems.Add(MenuItem.Sep);
            menuItems.Add(new MenuItem("", "<Interactions>"));
            foreach (var group in context.InteractionGroups) {
                // Add visible interactions directly
                foreach (var action in group.Actions.Where(a => a.IsVisible)) {
                    menuItems.Add(new ActionMenuItem(
                        action.OptionCode,
                        action.Description,
                        args => action.Perform(args),
                        action.IsEnabled ? EnableArg.Enabled : EnableArg.Disabled,
                        ShowArg.Show
                    ));
                }

                menuItems.Add(MenuItem.Sep);

                // Add group summary entry
                var summaryAction = context.Lookup(group.Prefix);
                if (summaryAction != null) {
                    menuItems.Add(new ActionMenuItem(
                        group.Prefix,
                        group.Label,
                        args => summaryAction.Perform(args),
                        group.HasEnabledActions ? EnableArg.Enabled : EnableArg.Disabled,
                        ShowArg.Show
                    ));
                }

                menuItems.Add(MenuItem.Sep);
            }
        }

        menuItems.Add(new MenuItem("", "Choose"));

        // Render menu and get input
        return RenderMenuItems(menuItems, context, defaultOption, title);
    }

    /// <summary>
    /// Shared menu rendering and input processing for MenuContext.
    /// </summary>
    private MenuSelection RenderMenuItems(List<MenuItem> menuItems, MenuContext context, string defaultOption, string title) {
        // No enabled options - wait for enter
        int enabledCount = menuItems.Count(m => m.IsEnabled);
        if (enabledCount == 0) {
            Console.WriteLine("No options available. Press enter to continue.");
            Console.ReadLine();
            return MenuSelection.Cancel;
        }

        // Display menu
        bool start = false;
        foreach (var item in menuItems) {
            if (!item.IsShow) continue;
            if (item.Option == "") {
                start = true;
            }
            if (item.Item == "") {
                start = true;
            } else {
                Console.Write(start ? '\n' : ' ');
                var itemFormat = item.Format();
                Console.Write(itemFormat);
                start = itemFormat.EndsWith('\n');
            }
        }

        // Get user input
        string input;
        try {
            do {
                input = CrawlerEx.Input("? ", defaultOption);
            } while (input == "");
        } catch (EndOfStreamException) {
            return MenuSelection.Cancel;
        }

        Console.Write(Style.MenuNormal.StyleString());

        // Parse input: split option code from arguments
        var firstSpace = input.IndexOf(' ');
        string arguments = "";
        if (firstSpace > 0) {
            arguments = input.Substring(firstSpace + 1).Trim();
            input = input.Substring(0, firstSpace);
        }

        // Try action lookup first
        var action = context.Lookup(input);
        if (action != null) {
            // Execute action directly
            var ap = action.Perform(arguments);
            // Return with interaction if it's an InteractionAction
            if (action is InteractionAction intAction) {
                return MenuSelection.Select(intAction.Interaction, arguments);
            }
            // For non-interaction actions, use the action's return value to determine
            // whether to exit the menu loop. Time advancement is handled by scheduling.
            return ap ? new MenuSelection { ShouldExitMenu = true } : MenuSelection.Cancel;
        }

        // Unrecognized input
        Console.WriteLine(Style.SegmentDestroyed.Format($"Unrecognized input '{input}'"));
        return Render(context, title, defaultOption); // Retry
    }

    /// <summary>
    /// Legacy: Render InteractionContext (backward compatibility).
    /// </summary>
    public MenuSelection Render(InteractionContext context, string title = "", string defaultOption = "") {
        if (title != "") {
            Console.Write(Style.MenuTitle.Format($"{title}"));
        }
        Console.Write(Style.MenuNormal.StyleString());

        // Build flat menu items from interaction groups
        var menuItems = new List<MenuItem>();

        foreach (var group in context.Groups) {
            // Process immediate interactions (ultimatums, mandatory actions)
            foreach (var interaction in group.Interactions) {
                var msg = interaction.MessageFor(context.Agent);
                if (!string.IsNullOrEmpty(msg)) {
                    context.Agent.Message(Style.Em.Format(msg));
                }
                if (interaction.GetImmediacy() == Immediacy.Immediate) {
                    // Immediate interactions execute automatically
                    var ap = interaction.Perform();
                    // Return immediately - these interrupt normal flow
                    return new MenuSelection {
                        SelectedInteraction = interaction,
                        Arguments = ""
                    };
                }
            }

            // Generate menu items for this group
            var groupMenuItems = GenerateGroupMenuItems(group, context);
            menuItems.AddRange(groupMenuItems);
        }

        // No enabled options - wait for enter
        int enabledCount = menuItems.Count(m => m.IsEnabled);
        if (enabledCount == 0) {
            Console.WriteLine("No options available. Press enter to continue.");
            Console.ReadLine();
            return MenuSelection.Cancel;
        }

        // Display menu and get selection
        bool start = false;
        foreach (var item in menuItems) {
            if (!item.IsShow) continue;
            if (item.Option == "") {
                start = true;
            }
            if (item.Item == "") {
                start = true;
            } else {
                Console.Write(start ? '\n' : ' ');
                var itemFormat = item.Format();
                Console.Write(itemFormat);
                start = itemFormat.EndsWith('\n');
            }
        }

        // Get user input
        string input;
        try {
            do {
                input = CrawlerEx.Input("? ", defaultOption);
            } while (input == "");
        } catch (EndOfStreamException) {
            return MenuSelection.Cancel;
        }

        Console.Write(Style.MenuNormal.StyleString());

        // Parse input: split option code from arguments
        var firstSpace = input.IndexOf(' ');
        string arguments = "";
        if (firstSpace > 0) {
            arguments = input.Substring(firstSpace + 1).Trim();
            input = input.Substring(0, firstSpace);
        }

        // Match against menu items first (for non-interaction items)
        foreach (var item in menuItems) {
            if (string.Compare(item.Option, input, StringComparison.InvariantCultureIgnoreCase) == 0) {
                // If it's an ActionMenuItem, execute it
                if (item is ActionMenuItem action && action.IsEnabled) {
                    action.Run(arguments);
                }
                return MenuSelection.Cancel; // Non-interaction menu items cancel
            }
        }

        // Try interaction lookup
        var selectedInteraction = context.Lookup(input);
        if (selectedInteraction != null) {
            return MenuSelection.Select(selectedInteraction, arguments);
        }

        // Unrecognized input
        Console.WriteLine(Style.SegmentDestroyed.Format($"Unrecognized input '{input}'"));
        return Render(context, title, defaultOption); // Retry
    }

    private List<MenuItem> GenerateGroupMenuItems(InteractionGroup group, InteractionContext context) {
        var result = new List<MenuItem>();
        var interactions = group.Interactions;

        if (interactions.Count == 0) {
            // No interactions - show disabled entry
            result.Add(new ActionMenuItem(
                group.Prefix,
                $"{group.Label}\n",
                _ => false,
                EnableArg.Disabled,
                ShowArg.Show));
            return result;
        }

        // Generate detail menu items (CA1, CA2, etc.)
        var show = interactions.Count > 4 ? ShowArg.Hide : ShowArg.Show;
        result.AddRange(GenerateDetailMenuItems(interactions, group.Prefix, context, show));
        result.Add(MenuItem.Sep);

        // Generate summary menu item (CA)
        bool anyEnabled = interactions.Any(i => i.GetImmediacy() == Immediacy.Menu);
        result.Add(new ActionMenuItem(
            group.Prefix,
            group.Label,
            args => {
                // Submenu for this group
                var subMenu = new List<MenuItem> { MenuItem.Cancel };
                subMenu.AddRange(GenerateDetailMenuItems(interactions, group.Prefix, context, ShowArg.Show, args));
                var (selected, ap) = CrawlerEx.MenuRun($"{group.Label}", subMenu.ToArray());
                return ap;
            },
            anyEnabled ? EnableArg.Enabled : EnableArg.Disabled,
            ShowArg.Show));
        result.Add(MenuItem.Sep);

        return result;
    }

    private IEnumerable<MenuItem> GenerateDetailMenuItems(
        List<Interaction> interactions,
        string prefix,
        InteractionContext context,
        ShowArg show,
        string args = "")
    {
        int counter = 1;
        foreach (var interaction in interactions) {
            var shortcut = $"{prefix}{counter}";
            counter++;

            // Register for lookup
            context.RegisterInteraction(shortcut, interaction);

            var immediacy = string.IsNullOrEmpty(args) ?
                interaction.GetImmediacy() :
                interaction.GetImmediacy(args);

            var description = interaction.Description;
            if (!string.IsNullOrEmpty(args)) {
                try {
                    description = interaction.Description + $" ({args})";
                } catch {
                    // Ignore errors in description generation
                }
            }

            yield return new ActionMenuItem(
                $"{shortcut}",
                description,
                a => interaction.Perform(a),
                immediacy.ToEnableArg(),
                show);
        }
    }
}

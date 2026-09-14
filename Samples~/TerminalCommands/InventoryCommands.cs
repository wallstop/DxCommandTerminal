namespace WallstopStudios.DxCommandTerminal.Samples
{
    using System.Collections.Generic;
    using Backend;
    using UnityEngine;

    /*
        Subcommands and live dynamic choices. `inventory` routes one
        registered command through add/remove/list (a bare `inventory` runs
        the parent fallback), and `pickup` completes item names from the
        current inventory, so Tab reflects the live game state.
     */
    public sealed class InventoryCommands : TerminalCommandSample
    {
        private readonly List<string> _items = new() { "pickaxe", "torch", "rope" };

        protected override void RegisterCommands()
        {
            Register(
                CommandBuilder
                    .Create("inventory", "Manage the demo inventory")
                    .Handler(
                        (context, arguments) =>
                            Terminal.Log(
                                "Try: inventory add <item> [count], inventory remove <item>, inventory list"
                            )
                    )
                    .Subcommand(
                        "add",
                        add =>
                            add.Arg<string>("item", spec => spec.Required())
                                .Arg<int>("count", spec => spec.Default(1).Range(1, 99))
                                .Handler(
                                    (context, arguments) =>
                                    {
                                        string item = arguments.Get<string>("item");
                                        int count = arguments.Get<int>("count");
                                        for (int i = 0; i < count; ++i)
                                        {
                                            _items.Add(item);
                                        }

                                        Terminal.Log("Added {0} x{1}.", item, count);
                                    }
                                )
                    )
                    .Subcommand(
                        "remove",
                        remove =>
                            remove
                                /*
                                    Dynamic choices on a fixed argument: the
                                    provider runs on every completion request, so
                                    remove completes only what the inventory
                                    actually holds right now.
                                 */
                                .Arg<string>(
                                    "item",
                                    spec => spec.Required().Choices(context => _items)
                                )
                                .Handler(
                                    (context, arguments) =>
                                    {
                                        string item = arguments.Get<string>("item");
                                        if (_items.Remove(item))
                                        {
                                            Terminal.Log("Removed {0}.", item);
                                            return;
                                        }

                                        Terminal.Log("No {0} in the inventory.", item);
                                    }
                                )
                    )
                    .Subcommand("list", list => list.Handler((context, arguments) => LogItems()))
            );

            Register(
                CommandBuilder
                    .Create("pickup", "Picks up an item")
                    .Arg<string>("item", spec => spec.Required().Choices(context => _items))
                    .Arg<string>(
                        "action",
                        spec => spec.Required().Choices("equip", "inspect", "drop")
                    )
                    .Handler(
                        (context, arguments) =>
                        {
                            string item = arguments.Get<string>("item");
                            string action = arguments.Get<string>("action");
                            if (!_items.Remove(item))
                            {
                                Terminal.Log("Nothing to pick up: {0}.", item);
                                return;
                            }

                            Terminal.Log("{0} -> {1}.", action, item);
                        }
                    )
            );
        }

        private void LogItems()
        {
            if (_items.Count == 0)
            {
                Terminal.Log("The inventory is empty.");
                return;
            }

            Terminal.Log("Inventory: {0}", string.Join(", ", _items));
        }
    }
}

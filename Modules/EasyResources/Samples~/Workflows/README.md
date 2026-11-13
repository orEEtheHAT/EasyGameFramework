# Workflows

**Package:** EasyGameFramework. Requires the standard UI Toolkit module. No prerequisite sample imports.

Import this sample once, open `ResourceWorkflows.unity`, press Play. The scene runs 18 automatic checks and shows an event-driven Resource Panel. No gameplay input or prerequisite adapter imports are required. UI Toolkit must be enabled in Package Manager (it is a standard Unity module).

`ResourceWorkflowsDemo.Run()` is an executable collection of public API examples. It creates temporary definitions for the demonstration. In a game, create reusable Resource, Item, Recipe, Trade Catalog and Progression assets through the Create menu, and assign them in the Inspector.

To create another scene without duplicating scripts, use **Tools > EasyGameFramework > Resources > Create Workflows Scene**. Persistent operations require stable owner IDs; transport authentication and durable save storage are provided by the application and EasySave.

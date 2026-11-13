using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyBuilding.Tests
{
    public sealed class AuthoringTypeTests
    {
        [Test] public void EveryConcreteCoreComponentAndAssetHasAnImportableScript()
        {
            var imported = new HashSet<System.Type>();
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts()) if (script != null && script.GetClass() != null) imported.Add(script.GetClass());
            foreach (var type in typeof(BuildingSystem).Assembly.GetTypes())
                if (!type.IsAbstract && (typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type)))
                    Assert.That(imported.Contains(type), Is.True, type.Name + " must have a matching script file so scenes/prefabs can serialize it and users can add it in the Inspector.");
        }
    }
}

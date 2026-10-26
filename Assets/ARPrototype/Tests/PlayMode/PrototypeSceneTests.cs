using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARPrototype.Tests
{
    public sealed class PrototypeSceneTests
    {
        private Scene loadedScene;

        [UnityTest]
        public IEnumerator SavedARSceneAndPrefabCanLaunchAndAttach()
        {
            yield return SceneManager.LoadSceneAsync("ARThrowPrototype", LoadSceneMode.Additive);
            loadedScene = SceneManager.GetSceneByName("ARThrowPrototype");
            Assert.That(loadedScene.isLoaded, Is.True);
            ProjectileSpawner spawner = null;
            foreach (GameObject root in loadedScene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out ProjectileSpawner candidate)) spawner = candidate;
            }
            Assert.That(spawner != null, Is.True);
            spawner.enabled = false;
            Assert.That(spawner.ARCamera != null, Is.True);
            Assert.That(spawner.ARCamera.transform.parent.parent.name, Is.EqualTo("XR Origin (Mobile AR)"));
            Assert.That(spawner.ProjectilePrefab != null, Is.True);
            Assert.That(spawner.SelectedTarget != null, Is.True);
            Assert.That(spawner.SelectedTarget.localScale, Is.EqualTo(Vector3.one));
            int layer = spawner.ProjectilePrefab.gameObject.layer;
            Assert.That(LayerMask.LayerToName(layer), Is.EqualTo("Projectile"));
            Assert.That(Physics.GetIgnoreLayerCollision(layer, layer), Is.True);
            Assert.That(Physics.GetIgnoreLayerCollision(layer, 0), Is.False);

            Vector3 screenPoint = spawner.ARCamera.WorldToScreenPoint(spawner.SelectedTarget.position);
            Projectile projectile = spawner.ThrowAtScreenPoint(screenPoint);
            Assert.That(projectile != null, Is.True);
            float deadline = Time.time + 1f;
            while (projectile != null && projectile.State == Projectile.FlightState.Flying && Time.time < deadline)
                yield return new WaitForFixedUpdate();
            Assert.That(projectile != null, Is.True);
            Assert.That(projectile.State, Is.EqualTo(Projectile.FlightState.Attached));
            Assert.That(projectile.transform.parent, Is.SameAs(spawner.SelectedTarget));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loadedScene.IsValid() && loadedScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(loadedScene);
        }
    }
}

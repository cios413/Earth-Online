using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ARPrototype.Tests
{
    public sealed class ProjectilePhysicsTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private Transform target;

        [SetUp]
        public void SetUp()
        {
            target = Track(new GameObject("SelectedTarget")).transform;
            target.position = new Vector3(0f, 0f, 1.5f);
            var body = target.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var shape = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            shape.transform.SetParent(target, false);
            shape.transform.localScale = new Vector3(0.6f, 0.4f, 0.2f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [UnityTest]
        public IEnumerator FrontHitAttachesAtSurfaceAndDisablesPhysics()
        {
            Projectile projectile = Shoot(Vector3.zero, Vector3.forward * 6f);
            yield return WaitForHit(projectile);
            AssertAttached(projectile);
            Assert.That(projectile.transform.position.z, Is.EqualTo(1.379f).Within(0.004f));
            Assert.That(Vector3.Dot(projectile.transform.forward, Vector3.back), Is.GreaterThan(0.999f));
            Assert.That(projectile.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(projectile.GetComponent<Rigidbody>().detectCollisions, Is.False);
            Assert.That(projectile.GetComponent<BoxCollider>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator AngledHitsAlignWithFrontSideAndTopSurfaces()
        {
            Vector3[] starts = { new Vector3(-0.2f, 0.1f, 0f),
                new Vector3(1f, 0.05f, 1.5f), new Vector3(0.05f, 1f, 1.5f) };
            Vector3[] normals = { Vector3.back, Vector3.right, Vector3.up };
            for (int i = 0; i < starts.Length; i++)
            {
                Projectile projectile = Shoot(starts[i], (target.position - starts[i]).normalized * 6f);
                yield return WaitForHit(projectile);
                AssertAttached(projectile);
                Assert.That(Vector3.Dot(projectile.transform.forward, normals[i]), Is.GreaterThan(0.999f));
                Vector3 local = target.InverseTransformPoint(projectile.transform.position);
                float distance = i == 0 ? -local.z - 0.1f : i == 1 ? local.x - 0.3f : local.y - 0.2f;
                Assert.That(distance, Is.EqualTo(0.021f).Within(0.004f));
            }
        }

        [UnityTest]
        public IEnumerator AttachedProjectileFollowsTranslationAndRotation()
        {
            Projectile projectile = Shoot(Vector3.zero, Vector3.forward * 6f);
            yield return WaitForHit(projectile);
            AssertAttached(projectile);
            Vector3 localPosition = projectile.transform.localPosition;
            Quaternion localRotation = projectile.transform.localRotation;
            target.SetPositionAndRotation(new Vector3(2f, 1f, 3f), Quaternion.Euler(25f, 72f, 16f));
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(Vector3.Distance(projectile.transform.position, target.TransformPoint(localPosition)), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(projectile.transform.rotation, target.rotation * localRotation), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator RotatedTargetHasCorrectLocalAttachmentPose()
        {
            target.rotation = Quaternion.Euler(15f, 32f, 10f);
            Physics.SyncTransforms();
            Vector3 normal = target.rotation * Vector3.back;
            Projectile projectile = Shoot(target.position + normal, -normal * 6f);
            yield return WaitForHit(projectile);
            AssertAttached(projectile);
            Assert.That(Vector3.Dot(projectile.transform.forward, normal), Is.GreaterThan(0.999f));
            Assert.That(projectile.transform.localPosition.z, Is.EqualTo(-0.121f).Within(0.004f));
        }

        [UnityTest]
        public IEnumerator ObstacleDestroysProjectileBeforeItCanReachTarget()
        {
            var obstacle = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            obstacle.transform.position = new Vector3(0f, 0f, 0.7f);
            obstacle.transform.localScale = new Vector3(0.5f, 0.5f, 0.1f);
            Physics.SyncTransforms();
            Projectile projectile = Shoot(Vector3.zero, Vector3.forward * 6f);
            yield return new WaitForSeconds(0.4f);
            Assert.That(projectile == null, Is.True);
            Assert.That(target.childCount, Is.EqualTo(1));
            Assert.That(obstacle.transform.childCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DifferentTargetReferenceCannotAttach()
        {
            var otherTarget = Track(new GameObject("OtherSelectedTarget"));
            Projectile projectile = Shoot(Vector3.zero, Vector3.forward * 6f, otherTarget.transform);
            yield return new WaitForSeconds(0.4f);
            Assert.That(projectile == null, Is.True);
            Assert.That(target.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator MissExpiresWithoutCreatingAttachment()
        {
            Projectile projectile = Shoot(Vector3.zero, Vector3.right * 6f);
            projectile.MaxLifetime = 0.12f;
            yield return new WaitForSeconds(0.25f);
            Assert.That(projectile == null, Is.True);
            Assert.That(target.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AttachedProjectileSurvivesFlightLifetime()
        {
            Projectile projectile = Shoot(new Vector3(0f, 0f, 1.1f), Vector3.forward * 6f);
            projectile.MaxLifetime = 0.2f;
            yield return WaitForHit(projectile);
            AssertAttached(projectile);
            yield return new WaitForSeconds(0.3f);
            AssertAttached(projectile);
        }

        [UnityTest]
        public IEnumerator HighSpeedProjectileDoesNotTunnelThroughTarget()
        {
            Projectile projectile = Shoot(Vector3.zero, Vector3.forward * 80f);
            yield return WaitForHit(projectile);
            AssertAttached(projectile);
        }

        [UnityTest]
        public IEnumerator RepeatedShotsEachProduceOneAttachment()
        {
            for (int i = 0; i < 5; i++)
            {
                Projectile projectile = Shoot(new Vector3((i - 2) * 0.06f, 0f, 0f), Vector3.forward * 6f);
                yield return WaitForHit(projectile);
                AssertAttached(projectile);
            }
            Assert.That(target.GetComponentsInChildren<Projectile>().Length, Is.EqualTo(5));
            yield return new WaitForFixedUpdate();
            Assert.That(target.GetComponentsInChildren<Projectile>().Length, Is.EqualTo(5));
        }

        [Test]
        public void DifferentScreenPointsProduceDifferentVelocitiesAndSnapshotTarget()
        {
            Camera camera = Track(new GameObject("TestCamera")).AddComponent<Camera>();
            camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            camera.nearClipPlane = 0.01f;
            Projectile template = NewProjectile(new Vector3(100f, 100f, 100f));
            template.GetComponent<Rigidbody>().isKinematic = true;
            ProjectileSpawner spawner = Track(new GameObject("Spawner")).AddComponent<ProjectileSpawner>();
            spawner.ARCamera = camera;
            spawner.SelectedTarget = target;
            spawner.ProjectilePrefab = template;
            Projectile left = spawner.ThrowAtScreenPoint(new Vector2(100f, 300f));
            Projectile right = spawner.ThrowAtScreenPoint(new Vector2(700f, 300f));
            Track(left.gameObject);
            Track(right.gameObject);
            Assert.That(left.GetComponent<Rigidbody>().velocity.x, Is.LessThan(0f));
            Assert.That(right.GetComponent<Rigidbody>().velocity.x, Is.GreaterThan(0f));
            spawner.SelectedTarget = null;
            Assert.That(left.SelectedTarget, Is.SameAs(target));
            Assert.That(spawner.Throw(Vector3.zero), Is.Null);
            Assert.That(spawner.Throw(new Vector3(float.NaN, 0f, 1f)), Is.Null);
        }

        private Projectile Shoot(Vector3 position, Vector3 velocity, Transform selected = null)
        {
            Projectile projectile = NewProjectile(position);
            projectile.Launch(selected == null ? target : selected, velocity);
            return projectile;
        }

        private Projectile NewProjectile(Vector3 position)
        {
            var cube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cube.transform.position = position;
            cube.transform.localScale = Vector3.one * 0.04f;
            cube.AddComponent<Rigidbody>();
            return cube.AddComponent<Projectile>();
        }

        private GameObject Track(GameObject item) { objects.Add(item); return item; }

        private static IEnumerator WaitForHit(Projectile projectile)
        {
            float deadline = Time.time + 1f;
            while (projectile != null && projectile.State == Projectile.FlightState.Flying && Time.time < deadline)
                yield return new WaitForFixedUpdate();
            yield return null;
        }

        private void AssertAttached(Projectile projectile)
        {
            Assert.That(projectile != null, Is.True, "Projectile was destroyed instead of attaching.");
            Assert.That(projectile.State, Is.EqualTo(Projectile.FlightState.Attached));
            Assert.That(projectile.transform.parent, Is.SameAs(target));
            Assert.That(Vector3.Distance(projectile.transform.localPosition, projectile.AttachedLocalPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(projectile.transform.localRotation, projectile.AttachedLocalRotation), Is.LessThan(0.01f));
        }
    }
}

using Monster.Presentation;
using Monster.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Monster.Tests
{
    /// <summary>Tests for the shape in the fog.
    ///
    /// The window and the biometric screen have to describe the same creature. A screen that
    /// reports five limbs over a silhouette with four is not a puzzle, it is a bug that looks
    /// like one, and a player who notices will stop trusting either.</summary>
    public sealed class SubjectFigureTests
    {
        private GameObject _root;
        private SubjectFigure _figure;
        private Transform[] _spares;
        private Transform[] _arms;
        private Transform _head;
        private Transform _neck;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Subject");

            Transform Part(string name)
            {
                var part = new GameObject(name).transform;
                part.SetParent(_root.transform, false);
                return part;
            }

            var torso = Part("Torso");
            _neck = Part("Neck");
            _neck.localPosition = new Vector3(0f, 1.82f, 0f);
            _neck.localScale = new Vector3(0.09f, 0.24f, 0.09f);
            _head = Part("Head");
            _head.localPosition = new Vector3(0f, 2.06f, 0f);
            _head.localScale = new Vector3(0.18f, 0.25f, 0.19f);
            _arms = new[] { Part("Arm_L"), Part("Arm_R") };
            _spares = new[] { Part("Spare_A"), Part("Spare_B") };

            foreach (var spare in _spares)
            {
                spare.gameObject.SetActive(false);
            }

            _figure = _root.AddComponent<SubjectFigure>();
            _figure.Configure(torso, _neck, _head, _arms, _spares);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        private static SubjectAttributes Subject(int limbs, string serial = "BT-1000", int born = 1988) =>
            new()
            {
                Name = "TEST BEARER",
                BirthYear = born,
                PermitSerial = serial,
                VisibleLimbCount = limbs,
                SpokenDistrictMatchesPermit = true,
            };

        [Test]
        public void FourLimbsShowsNoExtraArm()
        {
            _figure.Show(Subject(4));

            foreach (var spare in _spares)
            {
                Assert.IsFalse(spare.gameObject.activeSelf);
            }
        }

        [Test]
        public void TheWindowShowsWhatTheSweepCounted()
        {
            _figure.Show(Subject(5));
            Assert.IsTrue(_spares[0].gameObject.activeSelf);
            Assert.IsFalse(_spares[1].gameObject.activeSelf);

            _figure.Show(Subject(6));
            Assert.IsTrue(_spares[0].gameObject.activeSelf);
            Assert.IsTrue(_spares[1].gameObject.activeSelf);
        }

        /// <summary>A figure shown, replaced and shown again must return to exactly the
        /// shape it had. Without this the silhouette drifts as the player pages back and
        /// forth through a queue and stops being evidence.</summary>
        [Test]
        public void ShowingASubjectTwiceGivesTheSameShape()
        {
            var subject = Subject(4, "KX-4821", 1979);

            _figure.Show(subject);
            var firstArm = _arms[0].localScale;
            var firstHead = _head.localPosition;

            _figure.Show(Subject(6, "ZZ-0001", 1955));
            _figure.Show(subject);

            Assert.AreEqual(firstArm, _arms[0].localScale);
            Assert.AreEqual(firstHead, _head.localPosition);
        }

        [Test]
        public void DifferentBearersStandDifferently()
        {
            _figure.Show(Subject(4, "AA-1111", 1970));
            var first = _arms[0].localScale.y;

            _figure.Show(Subject(4, "QQ-9999", 1994));
            var second = _arms[0].localScale.y;

            Assert.AreNotEqual(first, second, "every bearer in the fog is the same shape");
        }

        /// <summary>The design asks for proportions that are slightly wrong, never a clear
        /// look. A figure that is obviously deformed answers the question the player is
        /// supposed to answer with paperwork.</summary>
        [Test]
        public void NoBearerIsObviouslyDeformed()
        {
            for (var i = 0; i < 400; i++)
            {
                _figure.Show(Subject(4, $"XX-{i:0000}", 1950 + i % 50));

                Assert.That(_arms[0].localScale.y, Is.InRange(0.95f, 1.25f),
                    "an arm long enough to decide a verdict on");
                Assert.That(_head.localPosition.y - 2.06f, Is.InRange(-0.01f, 0.15f),
                    "a head high enough to decide a verdict on");
            }
        }

        /// <summary>Limb counts outside the range the figure can show must not throw, because
        /// the generator is free to invent a number the art was never built for.</summary>
        [Test]
        public void AnImpossibleLimbCountIsClamped()
        {
            Assert.DoesNotThrow(() => _figure.Show(Subject(0)));
            Assert.DoesNotThrow(() => _figure.Show(Subject(99)));

            _figure.Show(Subject(99));
            foreach (var spare in _spares)
            {
                Assert.IsTrue(spare.gameObject.activeSelf);
            }
        }

        /// <summary>A box scales about its centre, so a neck stretched by a factor rises half
        /// as far as the head it is meant to reach. Past about a hand's worth of lift the head
        /// came off and floated, which is a bug that looks like art direction.</summary>
        [Test]
        public void TheHeadNeverComesOff()
        {
            for (var i = 0; i < 400; i++)
            {
                _figure.Show(Subject(4, $"NK-{i:0000}", 1960 + i % 40));

                var neckTop = _neck.localPosition.y + _neck.localScale.y * 0.5f;
                var headBottom = _head.localPosition.y - _head.localScale.y * 0.5f;

                Assert.GreaterOrEqual(neckTop, headBottom,
                    $"bearer NK-{i:0000} has a floating head: neck ends at {neckTop}, " +
                    $"head starts at {headBottom}");
            }
        }

    }
}

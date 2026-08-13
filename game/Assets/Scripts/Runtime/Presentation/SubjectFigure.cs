using Monster.Rules;
using UnityEngine;

namespace Monster.Presentation
{
    /// <summary>The shape standing in the fog.
    ///
    /// It was one fixed figure for every subject, which meant the window carried no
    /// information at all: the biometric screen could report five limbs and the thing outside
    /// would still have four. The instrument and the view have to agree, or looking up from
    /// the desk is decoration.
    ///
    /// The design asks for proportions that are slightly wrong -- long arms, high head --
    /// and never a clear look. So the variation is small and deterministic: the same bearer
    /// stands the same way every time the same night is replayed, and no single figure is
    /// odd enough to decide a verdict on by itself.</summary>
    public sealed class SubjectFigure : MonoBehaviour
    {
        [SerializeField] private Transform torso;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;
        [SerializeField] private Transform[] arms = new Transform[0];

        /// <summary>Limbs beyond the fourth. Hidden unless the readout says otherwise, and it
        /// is the readout that has to be right -- a guard who counts arms through fog and
        /// finds a different number than the screen has been lied to by one of them.</summary>
        [SerializeField] private Transform[] spareArms = new Transform[0];

        private Vector3[] _armRest;
        private Vector3 _torsoRest;
        private Vector3 _neckRest;
        private Vector3 _neckRestPosition;
        private Vector3 _headRest;
        private float _headHeight;
        private bool _captured;

        public void Configure(Transform torsoPart, Transform neckPart, Transform headPart,
            Transform[] armParts, Transform[] spareParts)
        {
            torso = torsoPart;
            neck = neckPart;
            head = headPart;
            arms = armParts ?? new Transform[0];
            spareArms = spareParts ?? new Transform[0];
        }

        private void CaptureRest()
        {
            if (_captured)
            {
                return;
            }

            _torsoRest = torso != null ? torso.localScale : Vector3.one;
            _neckRest = neck != null ? neck.localScale : Vector3.one;
            _neckRestPosition = neck != null ? neck.localPosition : Vector3.zero;
            _headRest = head != null ? head.localPosition : Vector3.zero;
            _headHeight = head != null ? head.localScale.y : 0f;

            _armRest = new Vector3[arms.Length];
            for (var i = 0; i < arms.Length; i++)
            {
                _armRest[i] = arms[i] != null ? arms[i].localScale : Vector3.one;
            }

            _captured = true;
        }

        public void Show(in SubjectAttributes subject)
        {
            CaptureRest();

            // Four limbs is two arms and two legs. Anything past that is an arm the fog does
            // not quite hide, and the count has to match what the sweep printed.
            var extra = Mathf.Clamp(subject.VisibleLimbCount - 4, 0, spareArms.Length);
            for (var i = 0; i < spareArms.Length; i++)
            {
                if (spareArms[i] != null)
                {
                    spareArms[i].gameObject.SetActive(i < extra);
                }
            }

            var seed = Hash(subject.PermitSerial, subject.BirthYear);

            // Deliberately small. A figure that is obviously deformed answers the question
            // the player is supposed to be answering with paperwork, and one that is
            // obviously normal makes the window pointless.
            var reach = 1f + Fraction(seed, 0) * 0.22f;
            var stoop = 1f - Fraction(seed, 1) * 0.10f;
            var lift = Fraction(seed, 2) * 0.14f;

            for (var i = 0; i < arms.Length; i++)
            {
                if (arms[i] != null)
                {
                    var rest = _armRest[i];
                    arms[i].localScale = new Vector3(rest.x, rest.y * reach, rest.z);
                }
            }

            if (torso != null)
            {
                torso.localScale = new Vector3(_torsoRest.x, _torsoRest.y * stoop, _torsoRest.z);
            }

            if (head != null)
            {
                head.localPosition = _headRest + new Vector3(0f, lift, 0f);
            }

            // The neck is stretched to reach wherever the head ended up rather than by a
            // factor that happens to look right. Scaling it by a guess left a gap: a box
            // scales about its centre, so the top of a stretched neck rises by half what the
            // head does, and above about a hand's worth of lift the head came off entirely.
            if (neck != null)
            {
                var bottom = _neckRestPosition.y - _neckRest.y * 0.5f;
                var headBottom = _headRest.y + lift - _headHeight * 0.5f;
                var length = Mathf.Max(_neckRest.y * 0.5f, headBottom - bottom + 0.015f);

                neck.localScale = new Vector3(_neckRest.x, length, _neckRest.z);
                neck.localPosition = new Vector3(
                    _neckRestPosition.x, bottom + length * 0.5f, _neckRestPosition.z);
            }
        }

        private static uint Hash(string serial, int birthYear)
        {
            unchecked
            {
                var hash = 2166136261u;

                foreach (var c in serial ?? string.Empty)
                {
                    hash = (hash ^ c) * 16777619u;
                }

                hash = (hash ^ (uint)birthYear) * 16777619u;
                return hash;
            }
        }

        private static float Fraction(uint seed, int index)
        {
            unchecked
            {
                var mixed = seed * (uint)(index * 2654435761u + 1u);
                mixed ^= mixed >> 15;
                return (mixed % 1000u) / 1000f;
            }
        }
    }
}

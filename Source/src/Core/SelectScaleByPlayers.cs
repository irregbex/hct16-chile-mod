using Aquiris.HorizonChase.Data;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.3.2: a car on the selection turntable with its own split-screen size (CarDef.SelectScale2P, the andas): full
    /// size with one player, Ratio of it with two or more. The views are built once at start-up, before the player
    /// count is known, so the size follows StaticData.NumPlayers while the view is shown.
    /// </summary>
    public class SelectScaleByPlayers : MonoBehaviour
    {
        public Vector3 Alone = Vector3.one;
        public float Ratio = 1f;

        private void LateUpdate()
        {
            int n = 1;
            try { n = StaticData.NumPlayers; } catch (System.Exception) { }
            Vector3 want = n > 1 ? Alone * Ratio : Alone;
            if (transform.localScale != want) transform.localScale = want;
        }
    }
}

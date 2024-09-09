using UnityEngine;

namespace Sanicball.UI
{
    public class Quitter : MonoBehaviour
    {
        public void Awake()
        {
#if WEBGL   // This won't work on WebGL so let's disable the button.
            gameObject.SetActive(false);
#endif
        }

        public void Quit()
        {
            // Beb
            Application.Quit();
        }
    }
}

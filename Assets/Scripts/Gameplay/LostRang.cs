using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SocialPlatforms;

namespace Sanicball
{
    public class LostRang : MonoBehaviour
    {
        private float velocity = -0.1f;
        private Rang rang;

        public void Awake()
        {
            this.rang = GetComponent<Rang>();
        }

        private void FixedUpdate()
        {
            if (rang != null && rang.isCollected) return;

            if (Physics.Raycast(transform.position, Vector3.down, out var hit, 1.5f, ~LayerMask.GetMask("Rang", "Racer", "Racer Ghost"), QueryTriggerInteraction.Ignore))
            {
                velocity = -(velocity * 0.66f);
                transform.position += Mathf.Abs(hit.distance - 1.498f) * Vector3.up;
            }
            else
            {
                velocity -= (15.0f * Time.deltaTime); // use real gravity for now
            }

            velocity = Mathf.Clamp(velocity, -30.0f, 30.0f);

            this.transform.rotation = Quaternion.Euler(0, Rangs.Instance.rotationY, 0);
            if (!Mathf.Approximately(velocity, 0f))
                this.transform.position += (Time.fixedDeltaTime * velocity * transform.up);
        }
    }
}

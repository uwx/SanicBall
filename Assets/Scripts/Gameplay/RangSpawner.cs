using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Sanicball.Gameplay;
using SanicballCore;
using UnityEngine;

public class RangSpawner : MonoBehaviour
{
    public GameObject rangPrefab;
    public bool snapToGround = true;
    public float groundOffset = 0.5f;

    public bool requireGrounded = false;

    public bool shouldRespawn = true;
    public float respawnTime = 10f;


    private Ball ball;

    protected virtual int RangCount => 1;
    protected virtual Vector3 GetPositionCore(int idx)
    {
        return transform.position;
    }

    protected Rang[] spawnedRangScript = null;
    protected GameObject[] spawnedRangs = null;

    public virtual void Start()
    {
        // this is really stupid but it works
        var collider = this.gameObject.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 350f;
        collider.includeLayers = LayerMask.GetMask("Racer");

        SpwanRangs();
        StartCoroutine(RespawnTimer());
    }

    public int balls = 0;
    public bool isVisible = true;

    private void OnTriggerEnter(Collider other)
    {
        var ball = other.GetComponent<Ball>();
        if (ball == null) return;

        if (ball.Type == BallType.Player && ball.CtrlType != ControlType.None)
            balls++;
    }

    private void OnTriggerExit(Collider other)
    {
        var ball = other.GetComponent<Ball>();
        if (ball == null) return;

        if (ball.Type == BallType.Player && ball.CtrlType != ControlType.None)
            balls--;
    }

    private void Update()
    {
        // keep track of the number of players inside the trigger collider
        // only render the rangs if there is at least one player inside, otherwise it's a waste of resources
        if (balls > 0 && !this.isVisible)
        {
            foreach (var rang in spawnedRangScript)
            {
                if (rang)
                    rang.SetVisible(true);
            }

            this.isVisible = true;
        }
        else if (balls == 0 && this.isVisible)
        {
            foreach (var rang in spawnedRangScript)
            {
                if (rang)
                    rang.SetVisible(false);
            }

            this.isVisible = false;
        }

        if (this.isVisible)
        {
            foreach (var rang in spawnedRangs)
                if (rang)
                    rang.transform.rotation = Quaternion.Euler(0, Rangs.Instance.rotationY, 0);
        }
    }

    public virtual void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        for (int i = 0; i < RangCount; i++)
        {
            var position = GetPositionCore(i);

            if (snapToGround && Physics.Raycast(position, -transform.up, out RaycastHit hit, 1000f, LayerMask.GetMask("Terrain")))
            {
                var pos = hit.point + (hit.normal * (0.5f + groundOffset));

                Gizmos.DrawLine(position, pos);
                Gizmos.DrawSphere(pos, 0.5f);
            }
            else if (snapToGround && requireGrounded)
            {
                return;
            }

            Gizmos.DrawSphere(position, 0.5f);
        }
    }

    public virtual void SpwanRangs()
    {
        spawnedRangs = new GameObject[RangCount];
        spawnedRangScript = new Rang[RangCount];

        for (int i = 0; i < RangCount; i++)
        {
            var position = GetPositionCore(i);
            spawnedRangScript[i] = SpwanRang(position);
            if (spawnedRangScript[i])
                spawnedRangs[i] = spawnedRangScript[i].gameObject; 
        }
    }

    private Rang SpwanRang(Vector3 position)
    {
        GameObject rang = null;
        if (snapToGround && Physics.Raycast(position, -transform.up, out RaycastHit hit, 100f, LayerMask.GetMask("Terrain")))
        {
            var pos = hit.point + (hit.normal * ((0.5f + groundOffset) * this.transform.localScale.y));
            rang = Instantiate(rangPrefab, pos, Quaternion.identity, this.transform);
        }
        else if (!snapToGround || !requireGrounded)
        {
            rang = Instantiate(rangPrefab, position, Quaternion.identity, this.transform);
        }

        if (!rang) return null;

        rang.transform.parent = transform;

        var rangScript = rang.GetComponent<Rang>();
        rangScript.spwaner = this;

        return rangScript;
    }

    public IEnumerator RespawnTimer()
    {
        //while (true)
        //{
        //    yield return new WaitForSeconds(respawnTime);

        //    for (int i = 0; i < rangCount; i++)
        //    {
        //        if (spawnedRangs[i] == null)
        //        {
        //            SpwanRang(getPosition(i));
        //        }
        //    }
        //}

        // TODO: this needs reworking to work with the new visibility system
        //       that said, do we really need to respawn rangs?
        yield return null;
    }
}

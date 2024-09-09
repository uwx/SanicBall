using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RangLineSpwaner : RangSpawner
{
    public int rangRows = 8;
    public int rangColumns = 2;

    public float rangRowSpacing = 4f;
    public float rangColumnSpacing = 4f;

    protected override int RangCount => this.rangRows * this.rangColumns;
    protected override Vector3 GetPositionCore(int idx)
    {
        int row = idx / rangColumns;
        int column = idx % rangColumns;

        return transform.position + rangRowSpacing * row * transform.forward + column * rangColumnSpacing * transform.right;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PjonkGooseEnemy.Misc;
public class MoveStunnedParticle : MonoBehaviour
{
    // Start is called before the first frame update
    public GameObject ParticleObject;
    public GameObject ParticleSocket;

    // Update is called once per frame
    private void Update()
    {
        ParticleObject.transform.position = ParticleSocket.transform.position;
    }

    public void ShowStunnedParticle(){
        ParticleObject.SetActive(true);
    }
    public void HideStunnedParticle(){
        ParticleObject.SetActive(false);
    }
}

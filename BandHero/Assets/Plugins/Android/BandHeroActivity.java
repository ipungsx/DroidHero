package com.bandhero.picker;

import android.content.Intent;
import android.os.Bundle;

import com.unity3d.player.UnityPlayerActivity;

/**
 * Unity's default activity does not forward file-picker results, so this subclass relays them
 * to {@link BandHeroFilePicker}.
 */
public class BandHeroActivity extends UnityPlayerActivity
{
    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);
        BandHeroFilePicker.initialize(this);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        super.onActivityResult(requestCode, resultCode, data);
        BandHeroFilePicker.onActivityResult(requestCode, resultCode, data);
    }
}

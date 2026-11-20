package com.droidhero.picker;

import android.content.Intent;
import android.os.Bundle;

import com.unity3d.player.UnityPlayerGameActivity;

/**
 * Unity's default activity does not forward file-picker results, so this subclass relays them
 * to {@link DroidHeroFilePicker}.
 */
public class DroidHeroActivity extends UnityPlayerGameActivity
{
    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);
        DroidHeroFilePicker.initialize(this);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        super.onActivityResult(requestCode, resultCode, data);
        DroidHeroFilePicker.onActivityResult(requestCode, resultCode, data);
    }
}

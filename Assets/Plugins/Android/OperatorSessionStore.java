package com.circuitosvr.auth;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Stores only encrypted refresh tokens; keys stay in this app's Android Keystore. */
public final class OperatorSessionStore {
    private static final String PREFERENCES = "circuitosvr_operator_sessions";
    private static final String PROVIDER = "AndroidKeyStore";

    private OperatorSessionStore() { }

    private static SharedPreferences preferences(Context context) {
        return context.getApplicationContext().getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE);
    }

    private static SecretKey key(String target, boolean create) throws Exception {
        KeyStore store = KeyStore.getInstance(PROVIDER);
        store.load(null);
        if (store.containsAlias(target)) return (SecretKey) store.getKey(target, null);
        if (!create) return null;
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, PROVIDER);
        generator.init(new KeyGenParameterSpec.Builder(target,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }

    public static synchronized String load(Context context, String target) throws Exception {
        String encrypted = preferences(context).getString(target, null);
        if (encrypted == null) return null;
        byte[] plain = null;
        try {
            SecretKey secret = key(target, false);
            if (secret == null) { clear(context, target); return null; }
            String[] parts = encrypted.split(":", -1);
            if (parts.length != 2) { clear(context, target); return null; }
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, secret,
                    new GCMParameterSpec(128, Base64.decode(parts[0], Base64.NO_WRAP)));
            cipher.updateAAD(target.getBytes(StandardCharsets.UTF_8));
            plain = cipher.doFinal(Base64.decode(parts[1], Base64.NO_WRAP));
            return new String(plain, StandardCharsets.UTF_8);
        } catch (java.security.GeneralSecurityException | IllegalArgumentException ex) {
            // An Android backup can restore preferences without their non-exportable key.
            // Discard only unusable local authorization and let the operator sign in again.
            clear(context, target);
            return null;
        } finally {
            if (plain != null) Arrays.fill(plain, (byte) 0);
        }
    }

    public static synchronized void save(Context context, String target, String token) throws Exception {
        if (token == null || token.trim().isEmpty()) throw new IllegalArgumentException("Missing token");
        byte[] plain = token.getBytes(StandardCharsets.UTF_8);
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, key(target, true));
            cipher.updateAAD(target.getBytes(StandardCharsets.UTF_8));
            String encrypted = Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP) + ":"
                    + Base64.encodeToString(cipher.doFinal(plain), Base64.NO_WRAP);
            // Persist rotation before returning the session to the menu/uploader.
            if (!preferences(context).edit().putString(target, encrypted).commit())
                throw new IllegalStateException("Could not save local authorization");
        } finally {
            Arrays.fill(plain, (byte) 0);
        }
    }

    public static synchronized void clear(Context context, String target) throws Exception {
        if (!preferences(context).edit().remove(target).commit())
            throw new IllegalStateException("Could not clear local authorization");
        KeyStore store = KeyStore.getInstance(PROVIDER);
        store.load(null);
        if (store.containsAlias(target)) store.deleteEntry(target);
    }
}

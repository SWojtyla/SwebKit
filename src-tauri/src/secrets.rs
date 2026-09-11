use keyring::Entry;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::Mutex;

const SERVICE: &str = "SwebKit";
const USERNAME: &str = "api-client-secrets";

// All secrets share one JSON blob under a single keychain entry (see `load_vault`/`save_vault`
// below), so every command here does a read-modify-write of the *whole* vault rather than a
// single key. Tauri dispatches plain (non-async) commands onto a thread pool, so two of these
// invoked close together — entirely realistic when a user sets auth on two different requests
// within a couple of seconds, which is exactly what their own debounce windows invite — can
// otherwise interleave: both read the same starting snapshot, both write back, and whichever
// finishes last silently drops the other's change. This mutex serializes the whole
// load-mutate-save sequence so that can't happen.
static VAULT_LOCK: Mutex<()> = Mutex::new(());

#[derive(Serialize, Deserialize, Default, Debug, Clone)]
struct Vault(HashMap<String, String>);

fn load_vault() -> Result<Vault, String> {
    let entry = Entry::new(SERVICE, USERNAME).map_err(|e| e.to_string())?;
    match entry.get_password() {
        Ok(json) => serde_json::from_str(&json).map_err(|e| e.to_string()),
        Err(keyring::Error::NoEntry) => Ok(Vault::default()),
        Err(e) => Err(e.to_string()),
    }
}

fn save_vault(vault: &Vault) -> Result<(), String> {
    let entry = Entry::new(SERVICE, USERNAME).map_err(|e| e.to_string())?;
    let json = serde_json::to_string(vault).map_err(|e| e.to_string())?;
    entry.set_password(&json).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn save_secret(key: String, secret: String) -> Result<(), String> {
    // Poison recovery is safe here: the mutex guards nothing but this critical section itself
    // (no shared data lives behind it), so a prior panic leaves nothing inconsistent to recover.
    let _guard = VAULT_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let mut vault = load_vault()?;
    vault.0.insert(key, secret);
    save_vault(&vault)
}

#[tauri::command]
pub fn get_secret(key: String) -> Result<Option<String>, String> {
    let _guard = VAULT_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let vault = load_vault()?;
    Ok(vault.0.get(&key).cloned())
}

#[tauri::command]
pub fn delete_secret(key: String) -> Result<(), String> {
    let _guard = VAULT_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let mut vault = load_vault()?;
    vault.0.remove(&key);
    if vault.0.is_empty() {
        // Best-effort cleanup of the keychain entry when no secrets remain.
        let _ = Entry::new(SERVICE, USERNAME).and_then(|e| e.delete_credential());
    } else {
        save_vault(&vault)?;
    }
    Ok(())
}

#[tauri::command]
pub fn list_secrets(prefix: Option<String>) -> Result<Vec<String>, String> {
    let _guard = VAULT_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let vault = load_vault()?;
    Ok(vault
        .0
        .keys()
        .filter(|k| prefix.as_ref().is_none_or(|p| k.starts_with(p)))
        .cloned()
        .collect())
}

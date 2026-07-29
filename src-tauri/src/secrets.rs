use keyring::Entry;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;

const SERVICE: &str = "SwebKit";
const USERNAME: &str = "api-client-secrets";

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
    let mut vault = load_vault()?;
    vault.0.insert(key, secret);
    save_vault(&vault)
}

#[tauri::command]
pub fn get_secret(key: String) -> Result<Option<String>, String> {
    let vault = load_vault()?;
    Ok(vault.0.get(&key).cloned())
}

#[tauri::command]
pub fn delete_secret(key: String) -> Result<(), String> {
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
    let vault = load_vault()?;
    Ok(vault
        .0
        .keys()
        .cloned()
        .filter(|k| prefix.as_ref().map_or(true, |p| k.starts_with(p)))
        .collect())
}

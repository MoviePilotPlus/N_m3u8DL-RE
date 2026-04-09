use chacha20::ChaCha20;
use chacha20::cipher::{KeyIvInit, StreamCipher};

pub fn decrypt_chacha20(data: &[u8], key: &[u8], nonce: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
    let mut cipher = ChaCha20::new_from_slices(key, nonce)?;
    let mut buffer = data.to_vec();
    cipher.apply_keystream(&mut buffer);
    Ok(buffer)
}

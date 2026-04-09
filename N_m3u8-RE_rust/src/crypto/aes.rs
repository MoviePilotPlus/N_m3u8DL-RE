use aes::cipher::{BlockDecryptMut, KeyInit, KeyIvInit};
use aes::Aes128;
use cbc::Decryptor;

pub fn decrypt_aes_128_cbc(data: &[u8], key: &[u8], iv: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
    let mut decryptor = Decryptor::<Aes128>::new_from_slices(key, iv)?;
    
    let mut buffer = data.to_vec();
    let chunks = buffer.chunks_mut(16).collect::<Vec<&mut [u8]>>();
    for chunk in chunks {
        decryptor.decrypt_block_mut(chunk.into());
    }
    
    // 去除PKCS#7填充
    if !buffer.is_empty() {
        let padding = buffer[buffer.len() - 1] as usize;
        if padding > 0 && padding <= 16 {
            buffer.truncate(buffer.len() - padding);
        }
    }
    
    Ok(buffer)
}

pub fn decrypt_aes_128_ecb(data: &[u8], key: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
    use ecb::Decryptor as EcbDecryptor;
    
    let mut decryptor = EcbDecryptor::<Aes128>::new_from_slice(key)?;
    
    let mut buffer = data.to_vec();
    let chunks = buffer.chunks_mut(16).collect::<Vec<&mut [u8]>>();
    for chunk in chunks {
        decryptor.decrypt_block_mut(chunk.into());
    }
    
    // 去除PKCS#7填充
    if !buffer.is_empty() {
        let padding = buffer[buffer.len() - 1] as usize;
        if padding > 0 && padding <= 16 {
            buffer.truncate(buffer.len() - padding);
        }
    }
    
    Ok(buffer)
}

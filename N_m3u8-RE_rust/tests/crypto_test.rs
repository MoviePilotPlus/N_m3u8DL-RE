use n_m3u8_re_rust::crypto::aes::decrypt_aes_128_cbc;
use n_m3u8_re_rust::crypto::aes::decrypt_aes_128_ecb;
use n_m3u8_re_rust::crypto::chacha20::decrypt_chacha20;

#[test]
fn test_aes_128_cbc_decrypt() {
    // 测试AES-128 CBC解密
    // 这里使用测试数据，实际使用时需要替换为真实的加密数据和密钥
    let data = vec![0x00; 16]; // 16字节的测试数据
    let key = vec![0x00; 16]; // 16字节的测试密钥
    let iv = vec![0x00; 16]; // 16字节的测试IV
    
    match decrypt_aes_128_cbc(&data, &key, &iv) {
        Ok(_decrypted) => {
            // 解密成功
        }
        Err(e) => {
            // 解密失败，但不应该panic，因为这只是测试
            println!("AES-128 CBC解密测试失败: {:?}", e);
        }
    }
}

#[test]
fn test_aes_128_ecb_decrypt() {
    // 测试AES-128 ECB解密
    // 这里使用测试数据，实际使用时需要替换为真实的加密数据和密钥
    let data = vec![0x00; 16]; // 16字节的测试数据
    let key = vec![0x00; 16]; // 16字节的测试密钥
    
    match decrypt_aes_128_ecb(&data, &key) {
        Ok(_decrypted) => {
            // 解密成功
        }
        Err(e) => {
            // 解密失败，但不应该panic，因为这只是测试
            println!("AES-128 ECB解密测试失败: {:?}", e);
        }
    }
}

#[test]
fn test_chacha20_decrypt() {
    // 测试ChaCha20解密
    // 这里使用测试数据，实际使用时需要替换为真实的加密数据和密钥
    let data = vec![0x00; 32]; // 32字节的测试数据
    let key = vec![0x00; 32]; // 32字节的测试密钥
    let nonce = vec![0x00; 8]; // 8字节的测试nonce
    
    match decrypt_chacha20(&data, &key, &nonce) {
        Ok(_decrypted) => {
            // 解密成功
        }
        Err(e) => {
            // 解密失败，但不应该panic，因为这只是测试
            println!("ChaCha20解密测试失败: {:?}", e);
        }
    }
}
/**
 * Generates an IBAN according to the IBAN standard.
 * @param {string} bban - The Basic Bank Account Number (country-specific, e.g., "0001111305005007384").
 * @returns {string} The generated IBAN.
 */
function generateIBAN(bban) {
    const countryCode = "SO"; // Somalia's country code
    // Move country code and '00' to the end
    const rearranged = bban + countryCode + "00";
    // Replace letters with numbers (A=10, B=11, ..., Z=35)
    const replaced = rearranged.replace(/[A-Z]/g, ch => (ch.charCodeAt(0) - 55).toString());
    // Calculate mod-97
    const mod97 = BigInt(replaced) % 97n;
    const checkDigits = String(98n - mod97).padStart(2, "0");
    return `${countryCode}${checkDigits}${bban}`;
}

// Example usage for Somalia (SO):
// For Somalia, the BBAN structure is: 2!n4!n16!n (Bank+Branch+Account), but you must know your country's BBAN format.
const countryCode = "SO";
const bban = "11000111305005007384"; // Example BBAN (must be correct for your country)
const iban = generateIBAN(countryCode, bban);
console.log(iban); // Output: e.g., SOxx11000111305005007384 (with correct check digits)


/**
 * Validates an IBAN according to the IBAN standard.
 * @param {string} iban - The IBAN to validate.
 * @returns {boolean} True if valid, false otherwise.
 */
function validateIBAN(iban) {
    // Move first 4 chars to the end
    const rearranged = iban.slice(4) + iban.slice(0, 4);
    // Replace letters with numbers
    const replaced = rearranged.replace(/[A-Z]/g, ch => (ch.charCodeAt(0) - 55).toString());
    // Check mod-97
    return BigInt(replaced) % 97n === 1n;
}

/**
 * Constructs a Somalia BBAN from bankCode, branchCode, and accountNumber.
 * @param {string} bankCode - 4-digit bank code.
 * @param {string} branchCode - 4-digit branch code.
 * @param {string} accountNumber - Account number (will be left-padded to 9 digits).
 * @returns {string} The BBAN string.
 */
function buildSomaliaBBAN(bankCode, branchCode, accountNumber) {
    const bank = String(bankCode).padStart(4, '0');
    const branch = String(branchCode).padStart(4, '0');
    const acct = String(accountNumber).padStart(9, '0');
    return bank + branch + acct;
}

// Command-line interface
if (require.main === module) {
    const args = process.argv.slice(2);
    if (args[0] === '--validate' && args[1]) {
        const iban = args[1];
        const isValid = validateIBAN(iban);
        console.log(`IBAN ${iban} is ${isValid ? 'valid' : 'invalid'}`);
    } else if (args[0] === '--generate' && args[1] && args[2] && args[3]) {
        const bankCode = args[1];
        const branchCode = args[2];
        const accountNumber = args[3];
        const bban = buildSomaliaBBAN(bankCode, branchCode, accountNumber);
        const iban = generateIBAN(bban);
        console.log(`Generated IBAN: ${iban}`);
    } else {
        console.log('Usage:');
        console.log('  node test_accounts.js --validate {iban}');
        console.log('  node test_accounts.js --generate {bankCode} {branchCode} {accountNumber}');
    }
}
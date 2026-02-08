import currencyCodes from 'currency-codes';

export interface CurrencyOption {
    value: string;
    label: string;
}

export const currencyOptions: CurrencyOption[] = currencyCodes
    .codes()

    .map((code): CurrencyOption | null => {
        const currency = currencyCodes.code(code);
        if (!currency || !currency.number) return null;

        return {
            value: currency.number,
            label: `${code} - ${currency.currency}`
        };
    })
    .filter((option): option is CurrencyOption => option !== null)
    .filter(
        (option, index, self) =>
            self.findIndex((t) => t.value === option.value) === index
    )
    .sort((a, b) => a.label.localeCompare(b.label));
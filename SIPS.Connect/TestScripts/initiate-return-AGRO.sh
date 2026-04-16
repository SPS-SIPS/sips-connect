#!/bin/bash

# Configuration
BASE_URL="http://localhost:8080" # Update this to your SIPS Connect Gateway URL
ENDPOINT="/api/v1/incoming"

# XML Payload
PAYLOAD=$(cat <<EOF
<FPEnvelope xmlns:header="urn:iso:std:iso:20022:tech:xsd:head.001.001.03"
    xmlns:document="urn:iso:std:iso:20022:tech:xsd:pacs.004.001.11"
    xmlns="urn:iso:std:iso:20022:tech:xsd:returnPayment_request">
    <header:AppHdr>
        <header:Fr>
            <header:FIId>
                <header:FinInstnId>
                    <header:Othr>
                        <header:Id>AGROSOS0</header:Id>
                    </header:Othr>
                </header:FinInstnId>
            </header:FIId>
        </header:Fr>
        <header:To>
            <header:FIId>
                <header:FinInstnId>
                    <header:Othr>
                        <header:Id>BDBKSOS0</header:Id>
                    </header:Othr>
                </header:FinInstnId>
            </header:FIId>
        </header:To>
        <header:BizMsgIdr>AGROSOS0RETURNE2E</header:BizMsgIdr>
        <header:MsgDefIdr>pacs.004.001.11</header:MsgDefIdr>
        <header:CreDt>$(date -u +"%Y-%m-%dT%H:%M:%SZ")</header:CreDt>
    </header:AppHdr>
    <document:Document>
        <document:PmtRtr>
            <document:GrpHdr>
                <document:MsgId>AGROSOS0RETURNMSGID</document:MsgId>
                <document:CreDtTm>$(date -u +"%Y-%m-%dT%H:%M:%SZ")</document:CreDtTm>
                <document:NbOfTxs>1</document:NbOfTxs>
                <document:SttlmInf>
                    <document:SttlmMtd>CLRG</document:SttlmMtd>
                    <document:ClrSys>
                        <document:Prtry>FP</document:Prtry>
                    </document:ClrSys>
                </document:SttlmInf>
                <document:PmtTpInf>
                    <document:LclInstrm>
                        <document:Prtry>CRTRM</document:Prtry>
                    </document:LclInstrm>
                    <document:CtgyPurp>
                        <document:Prtry>C2CCRT</document:Prtry>
                    </document:CtgyPurp>
                </document:PmtTpInf>
                <document:InstgAgt>
                    <document:FinInstnId>
                        <document:Othr>
                            <document:Id>AGROSOS0</document:Id>
                        </document:Othr>
                    </document:FinInstnId>
                </document:InstgAgt>
                <document:InstdAgt>
                    <document:FinInstnId>
                        <document:Othr>
                            <document:Id>BDBKSOS0</document:Id>
                        </document:Othr>
                    </document:FinInstnId>
                </document:InstdAgt>
            </document:GrpHdr>
            <document:TxInf>
                <document:RtrId>RTN-123456789</document:RtrId>
                <document:OrgnlTxId>BDBKSOS0610608639119258162522117</document:OrgnlTxId>
                <document:RtrRsnInf>
                    <document:Rsn>
                        <document:Prtry>AC03</document:Prtry>
                    </document:Rsn>
                    <document:AddtlInf>Inbound Return Test</document:AddtlInf>
                </document:RtrRsnInf>
            </document:TxInf>
        </document:PmtRtr>
    </document:Document>
</FPEnvelope>
EOF
)

echo "Executing return from AGROSOS0 to BDBKSOS0 for TxId: BDBKSOS0610608639119258162522117"
echo "--------------------------------------------------------"
echo "Payload:"
echo "$PAYLOAD"
echo "--------------------------------------------------------"

curl -s -w "\nHTTP Status: %{http_code}\n" -X POST "$BASE_URL$ENDPOINT" \
    -H 'Content-Type: application/xml' \
    -d "$PAYLOAD"

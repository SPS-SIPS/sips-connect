import ApiRequestTester from "../../component/ApiRequestTester/ApiRequestTester";
import RoleGuard from "../../auth/RoleGuard";

const Payment = () => {
  return (
      <RoleGuard allowedRoles={['gateway']}>
          <ApiRequestTester
              title={"API Payment Request Tester "}
              placeholder={"https://example.com/api/v1/Gateway/Status"}
              selectedRequest={"PaymentRequest"}
          />
      </RoleGuard>
  )
}

export default Payment;
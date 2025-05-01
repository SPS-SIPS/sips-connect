import ApiRequestTester from "../../component/ApiRequestTester/ApiRequestTester";
import RoleGuard from "../../auth/RoleGuard";

const Verify = () => {
  return (
      <RoleGuard allowedRoles={['gateway']}>
          <ApiRequestTester
              title={"API Verification Request Tester "}
              placeholder={"https://example.com/api/v1/Gateway/Status"}
              selectedRequest={"VerificationRequest"}  // ReturnRequest
          />
      </RoleGuard>
  )
}
export default Verify;
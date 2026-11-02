import { useEffect, type ReactNode } from 'react';
import { Link } from 'react-router';
import { paths } from '@/app/paths';
import { useDocumentTitle } from '@/hooks/useTheme';

const EMAIL = 'rongnhotranlation@gmail.com';
const UPDATED = '02/11/2026';

/**
 * Keep public information pages readable and consistent with the main site.
 * This client-side metadata is useful for navigation; the rest of the SPA
 * still needs server-rendered metadata for reliable crawler indexing.
 */
function useInfoMeta(description: string, route: string) {
  useEffect(() => {
    const descriptionTag = document.querySelector<HTMLMetaElement>('meta[name="description"]');
    const canonicalTag = document.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    const originalDescription = descriptionTag?.content;
    const originalCanonical = canonicalTag?.href;

    if (descriptionTag) descriptionTag.content = description;
    if (canonicalTag) canonicalTag.href = 'https://rongnho.shop' + route;

    return () => {
      if (descriptionTag && originalDescription !== undefined) descriptionTag.content = originalDescription;
      if (canonicalTag && originalCanonical !== undefined) canonicalTag.href = originalCanonical;
    };
  }, [description, route]);
}

function InfoPage({ title, intro, route, description, children }: {
  title: string;
  intro: string;
  route: string;
  description: string;
  children: ReactNode;
}) {
  useDocumentTitle(title);
  useInfoMeta(description, route);

  return (
    <article className="container-page mx-auto max-w-4xl py-8 sm:py-12">
      <div className="mb-8">
        <p className="mb-2 text-sm font-medium text-accent">Thông tin Rồng Nhỏ</p>
        <h1 className="text-3xl font-bold tracking-tight text-ink sm:text-4xl">{title}</h1>
        <p className="mt-4 max-w-3xl text-sm leading-7 text-muted sm:text-base">{intro}</p>
        <p className="mt-3 text-xs text-muted">Cập nhật lần cuối: {UPDATED}</p>
      </div>
      <div className="space-y-5">{children}</div>
      <div className="mt-8 rounded-2xl border border-line bg-surface-2 p-5 text-sm leading-7">
        Bạn cần hỗ trợ hoặc có câu hỏi về nội dung trên? Xem trang{' '}
        <Link className="link font-medium" to={paths.contact}>Liên hệ</Link> hoặc gửi email tới{' '}
        <a className="link break-all font-medium" href={'mailto:' + EMAIL}>{EMAIL}</a>.
      </div>
    </article>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-2xl border border-line bg-surface p-5 sm:p-6">
      <h2 className="mb-3 text-lg font-semibold text-ink">{title}</h2>
      <div className="space-y-3 text-sm leading-7 text-muted sm:text-base">{children}</div>
    </section>
  );
}

export function AboutPage() {
  return (
    <InfoPage title="Giới thiệu Rồng Nhỏ" route={paths.about}
      description="Tìm hiểu về Rồng Nhỏ, nền tảng đọc truyện tranh và nơi các nhóm dịch chia sẻ tác phẩm đến độc giả."
      intro="Rồng Nhỏ là nền tảng giúp độc giả khám phá truyện tranh dịch tiếng Việt và theo dõi những bộ truyện mình yêu thích trên điện thoại hoặc máy tính.">
      <Section title="Rồng Nhỏ mang đến điều gì?">
        <p>Website tập hợp danh sách truyện, thể loại, thông tin nhóm dịch và các chương được công bố để bạn tìm kiếm và đọc thuận tiện. Bạn có thể khám phá truyện mới cập nhật, xem truyện đã hoàn thành hoặc tìm theo tên và thể loại.</p>
        <p>Khi có tài khoản, bạn có thể theo dõi truyện, sử dụng tủ truyện và tiếp tục đọc những chương còn dang dở.</p>
      </Section>
      <Section title="Đồng hành cùng các nhóm dịch">
        <p>Rồng Nhỏ được thiết kế để các nhóm dịch có không gian giới thiệu nhóm và quản lý nội dung họ đăng tải. Thông tin nhóm dịch được hiển thị tại trang riêng, giúp độc giả biết ai là người thực hiện bản dịch và cập nhật tác phẩm.</p>
        <p>Việc đăng tải phải tôn trọng quyền của tác giả, họa sĩ, nhà xuất bản và các chủ thể quyền liên quan. Rồng Nhỏ không mặc nhiên sở hữu bản quyền đối với các tác phẩm xuất hiện trên nền tảng.</p>
      </Section>
      <Section title="Trải nghiệm đọc và đóng góp ý kiến">
        <p>Chúng tôi tập trung vào việc tìm truyện dễ dàng, bố cục đọc phù hợp nhiều kích thước màn hình và điều hướng giữa các chương rõ ràng. Các góp ý về trải nghiệm, thông tin truyện và lỗi hình ảnh đều được hoan nghênh.</p>
        <p>Để phản hồi, báo lỗi hoặc trao đổi về đăng tải nội dung, hãy sử dụng trang <Link className="link" to={paths.contact}>Liên hệ</Link>.</p>
      </Section>
    </InfoPage>
  );
}

export function PrivacyPage() {
  return (
    <InfoPage title="Chính sách bảo mật" route={paths.privacy}
      description="Chính sách bảo mật của Rồng Nhỏ: dữ liệu tài khoản, cookie, lịch sử đọc, đo lường lượt xem, quảng cáo và quyền của người dùng."
      intro="Chính sách này giải thích những thông tin Rồng Nhỏ có thể xử lý khi bạn truy cập, đăng ký hoặc sử dụng các tính năng của rongnho.shop.">
      <Section title="1. Thông tin chúng tôi xử lý">
        <ul className="list-disc space-y-2 pl-5">
          <li><strong>Thông tin tài khoản:</strong> tên đăng nhập, tên hiển thị, email, mật khẩu được lưu dưới dạng mã băm an toàn và thông tin đăng nhập cần thiết để bảo vệ tài khoản.</li>
          <li><strong>Hoạt động đọc:</strong> truyện đang theo dõi, trạng thái tủ truyện, thông tin chương đã đọc hoặc đọc tiếp và dữ liệu liên quan đến việc sử dụng tính năng.</li>
          <li><strong>Dữ liệu kỹ thuật:</strong> địa chỉ IP, thông tin trình duyệt/thiết bị, thời điểm và các tín hiệu cần thiết để bảo mật, giới hạn truy cập và xác thực lượt xem hợp lệ.</li>
          <li><strong>Thông tin bạn chủ động gửi:</strong> nội dung email hỗ trợ, yêu cầu gỡ bỏ, phản hồi hoặc dữ liệu được đăng tải trong phạm vi quyền tài khoản/nhóm dịch.</li>
        </ul>
        <p>Bạn có thể đọc nội dung công khai mà không cần tạo tài khoản; một số tính năng cá nhân yêu cầu đăng nhập.</p>
      </Section>
      <Section title="2. Mục đích sử dụng">
        <p>Thông tin được xử lý để cung cấp và duy trì dịch vụ đọc truyện; đăng nhập, quản lý tài khoản và tủ truyện; hỗ trợ người dùng; bảo vệ hệ thống trước hành vi lạm dụng; thống kê lượt xem; giải quyết tranh chấp và đáp ứng nghĩa vụ pháp luật khi áp dụng.</p>
        <p>Dữ liệu về lượt đọc có thể được dùng để tổng hợp thống kê cho nhóm dịch và vận hành cơ chế ghi nhận lượt xem. Thông tin nhận dạng mạng/trình duyệt có thể được xử lý thành giá trị xác minh nhằm hạn chế lượt xem giả.</p>
      </Section>
      <Section title="3. Cookie và bộ nhớ trình duyệt">
        <p>Rồng Nhỏ sử dụng cookie phiên đăng nhập (bao gồm cookie làm mới phiên với thuộc tính HttpOnly) và bộ nhớ trình duyệt để duy trì những lựa chọn như giao diện sáng/tối, chế độ đọc và nhóm dịch đang quản lý.</p>
        <p>Bạn có thể xóa cookie và dữ liệu trang trong cài đặt trình duyệt. Việc này có thể khiến bạn phải đăng nhập lại hoặc mất các tùy chọn chỉ lưu trên thiết bị hiện tại.</p>
      </Section>
      <Section title="4. Quảng cáo và dịch vụ bên thứ ba">
        <p>Website có tích hợp mã Google AdSense và có thể sử dụng dịch vụ phân phối nội dung, bảo mật/chống bot hoặc mạng quảng cáo khác tùy cấu hình. Khi các dịch vụ này hoạt động, đối tác có thể xử lý cookie, địa chỉ IP và dữ liệu thiết bị theo chính sách của họ.</p>
        <p>Google có thể sử dụng cookie hoặc công nghệ tương tự để phân phối và đo lường quảng cáo. Bạn có thể xem thông tin tại <a className="link" href="https://policies.google.com/technologies/partner-sites" target="_blank" rel="noopener noreferrer">cách Google sử dụng dữ liệu từ các trang đối tác</a> và quản lý tùy chọn quảng cáo tại <a className="link" href="https://myadcenter.google.com/" target="_blank" rel="noopener noreferrer">Trung tâm quảng cáo của Google</a>.</p>
        <p>Chính sách của các đơn vị bên thứ ba được áp dụng riêng. Việc website có tích hợp mã quảng cáo không có nghĩa quảng cáo luôn được hiển thị.</p>
      </Section>
      <Section title="5. Bảo mật, lưu giữ và chia sẻ">
        <p>Chúng tôi áp dụng các biện pháp kỹ thuật để giảm nguy cơ truy cập trái phép, trong đó có lưu mật khẩu dưới dạng mã băm và sử dụng cơ chế xác thực phiên. Không có hệ thống trực tuyến nào bảo đảm an toàn tuyệt đối.</p>
        <p>Thông tin được lưu trong thời gian cần thiết cho hoạt động tài khoản, bảo vệ hệ thống, thống kê hợp lệ hoặc xử lý nghĩa vụ và tranh chấp liên quan. Một số dữ liệu có thể được xử lý bởi nhà cung cấp hạ tầng và dịch vụ kỹ thuật nhằm vận hành website.</p>
      </Section>
      <Section title="6. Quyền của bạn">
        <p>Bạn có thể yêu cầu xem, chỉnh sửa hoặc xóa thông tin cá nhân của mình theo quy định áp dụng. Để xác minh đúng chủ tài khoản, chúng tôi có thể yêu cầu thông tin cần thiết trước khi xử lý. Một số dữ liệu có thể phải được lưu lại khi pháp luật yêu cầu hoặc để giải quyết yêu cầu hợp lệ.</p>
        <p>Vui lòng gửi yêu cầu tới <a className="link" href={'mailto:' + EMAIL}>{EMAIL}</a>, nêu rõ tài khoản và loại yêu cầu. Không gửi mật khẩu qua email.</p>
      </Section>
      <Section title="7. Cập nhật chính sách">
        <p>Chính sách có thể được sửa đổi để phù hợp với thay đổi của website, dịch vụ bên thứ ba và quy định áp dụng. Phiên bản đang có hiệu lực được công bố tại trang này cùng ngày cập nhật.</p>
      </Section>
    </InfoPage>
  );
}

export function ContactPage() {
  return (
    <InfoPage title="Liên hệ" route={paths.contact}
      description="Liên hệ Rồng Nhỏ để báo lỗi truyện, hỗ trợ tài khoản, góp ý và gửi yêu cầu xử lý bản quyền."
      intro="Rồng Nhỏ tiếp nhận phản hồi về lỗi đọc truyện, tài khoản, hợp tác nhóm dịch, quyền tác giả và những vấn đề khác liên quan đến website.">
      <Section title="Kênh liên hệ chính thức">
        <p>Gửi email tới địa chỉ sau. Đây là kênh tiếp nhận yêu cầu hỗ trợ và khiếu nại của Rồng Nhỏ:</p>
        <p className="break-all text-lg font-semibold text-accent"><a href={'mailto:' + EMAIL}>{EMAIL}</a></p>
        <p>Bạn có thể <a className="link" href={'mailto:' + EMAIL + '?subject=Ho%20tro%20Rong%20Nho'}>mở ứng dụng email để gửi yêu cầu</a>.</p>
      </Section>
      <Section title="Khi cần báo lỗi truyện hoặc tài khoản">
        <p>Hãy gửi đường dẫn trang hoặc chương gặp lỗi, tên truyện, mô tả hiện tượng và ảnh chụp màn hình nếu có. Với vấn đề tài khoản, cung cấp tên đăng nhập hoặc email đăng ký nhưng <strong>không gửi mật khẩu, mã phiên hoặc mã xác thực</strong>.</p>
      </Section>
      <Section title="Yêu cầu bản quyền hoặc gỡ bỏ nội dung">
        <p>Nếu bạn là chủ sở hữu quyền hoặc đại diện hợp pháp, vui lòng nêu tên tác phẩm, các URL cụ thể trên Rồng Nhỏ, quyền mà bạn sở hữu, căn cứ yêu cầu và thông tin liên hệ để chúng tôi có thể xác minh và phản hồi.</p>
        <p>Chúng tôi sẽ xem xét thông báo hợp lệ và thực hiện biện pháp phù hợp, bao gồm hạn chế truy cập hoặc gỡ bỏ nội dung khi cần thiết. Việc ghi nguồn hoặc đề nghị gỡ bỏ không thay thế giấy phép sử dụng tác phẩm.</p>
      </Section>
    </InfoPage>
  );
}

export function TermsPage() {
  return (
    <InfoPage title="Điều khoản sử dụng" route={paths.terms}
      description="Điều khoản sử dụng của Rồng Nhỏ về tài khoản, nội dung nhóm dịch, bản quyền, hành vi bị cấm và trách nhiệm người dùng."
      intro="Khi truy cập và sử dụng Rồng Nhỏ, bạn đồng ý tuân thủ các điều khoản dưới đây và các quy định pháp luật có liên quan.">
      <Section title="1. Phạm vi sử dụng">
        <p>Rồng Nhỏ cung cấp nền tảng đọc truyện tranh và các công cụ giúp người dùng khám phá tác phẩm, theo dõi truyện và, với tài khoản được phân quyền, quản lý nội dung của nhóm dịch. Tính năng có thể được điều chỉnh trong quá trình vận hành.</p>
      </Section>
      <Section title="2. Tài khoản và an toàn thông tin">
        <p>Hãy cung cấp thông tin đăng ký chính xác, bảo mật mật khẩu và không sử dụng tài khoản của người khác khi chưa được phép. Bạn chịu trách nhiệm về hoạt động diễn ra qua tài khoản của mình trong phạm vi pháp luật cho phép.</p>
        <p>Rồng Nhỏ có thể tạm hạn chế hoặc vô hiệu hóa tài khoản có dấu hiệu lạm dụng, vi phạm điều khoản hoặc gây rủi ro bảo mật, đồng thời xem xét các phản hồi liên quan.</p>
      </Section>
      <Section title="3. Quy tắc sử dụng và đăng tải">
        <ul className="list-disc space-y-2 pl-5">
          <li>Không đăng nội dung trái pháp luật; quấy rối, đe dọa, phân biệt đối xử, xâm phạm đời tư hoặc giả mạo người khác.</li>
          <li>Không phát tán phần mềm độc hại, khai thác lỗ hổng, can thiệp hệ thống hay tạo lượt xem tự động/gian lận.</li>
          <li>Không đăng tải nội dung vi phạm quyền tác giả, quyền liên quan, nhãn hiệu hoặc các quyền hợp pháp khác.</li>
          <li>Không thu thập trái phép dữ liệu cá nhân hoặc sử dụng nền tảng để gửi thư rác và quảng cáo không được phép.</li>
        </ul>
      </Section>
      <Section title="4. Quyền tác giả và nội dung nhóm dịch">
        <p>Quyền đối với tác phẩm gốc thuộc về tác giả, nhà xuất bản hoặc các chủ sở hữu tương ứng; quyền đối với phần đóng góp của nhóm dịch được xác định theo pháp luật và các thỏa thuận áp dụng.</p>
        <p>Nhóm đăng tải có trách nhiệm bảo đảm mình có quyền hoặc sự cho phép cần thiết để chia sẻ nội dung trên Rồng Nhỏ. Việc nội dung xuất hiện trên website không có nghĩa Rồng Nhỏ xác nhận nội dung đó đã được cấp phép.</p>
        <p>Đối với khiếu nại về quyền tác giả hoặc yêu cầu gỡ bỏ, xem hướng dẫn tại <Link className="link" to={paths.contact}>Liên hệ</Link>.</p>
      </Section>
      <Section title="5. Nội dung dành cho người trưởng thành">
        <p>Một số truyện được đánh dấu 18+ hoặc chứa chủ đề nhạy cảm. Người dùng có trách nhiệm tuân thủ giới hạn độ tuổi và pháp luật nơi mình truy cập. Xác nhận độ tuổi trên website là xác nhận do người dùng tự cung cấp, không phải quy trình xác minh danh tính.</p>
      </Section>
      <Section title="6. Tính khả dụng và thay đổi dịch vụ">
        <p>Chúng tôi cố gắng duy trì website ổn định nhưng không cam kết mọi nội dung hoặc tính năng luôn sẵn có, không gián đoạn hoặc không có lỗi. Liên kết và dịch vụ bên thứ ba tuân theo điều kiện riêng của họ.</p>
        <p>Rồng Nhỏ có thể cập nhật điều khoản này khi cần; phiên bản mới được đăng tại trang hiện tại. Nếu không đồng ý với điều khoản, bạn nên ngừng sử dụng dịch vụ.</p>
      </Section>
      <Section title="7. Chính sách liên quan">
        <p>Vui lòng đọc thêm <Link className="link" to={paths.privacy}>Chính sách bảo mật</Link> để biết cách thông tin được xử lý và <Link className="link" to={paths.faq}>FAQ</Link> để tìm hướng dẫn sử dụng.</p>
      </Section>
    </InfoPage>
  );
}

const questions: { question: string; answer: ReactNode }[] = [
  {
    question: 'Có cần đăng ký tài khoản để đọc truyện không?',
    answer: <>Bạn có thể đọc các truyện công khai mà không cần đăng nhập. Tài khoản giúp bạn theo dõi truyện, lưu tủ truyện và sử dụng các tiện ích cá nhân.</>,
  },
  {
    question: 'Làm thế nào để tìm một bộ truyện?',
    answer: <>Sử dụng ô tìm kiếm theo tên truyện hoặc tác giả, vào <Link className="link" to={paths.browse}>Danh sách truyện</Link> hay lựa chọn các thể loại để lọc.</>,
  },
  {
    question: 'Làm sao lưu truyện yêu thích hoặc đọc tiếp?',
    answer: <>Đăng nhập và chọn nút <strong>Theo dõi</strong> ở trang chi tiết truyện. Bạn có thể vào <Link className="link" to={paths.library}>Tủ truyện</Link> để xem truyện đã theo dõi và tiếp tục đọc khi có lịch sử phù hợp.</>,
  },
  {
    question: 'Vì sao hình ảnh hoặc chương truyện không tải được?',
    answer: <>Thử tải lại trang, kiểm tra kết nối Internet và mở lại chương. Nếu chỉ một chương bị lỗi, gửi URL, số chương và ảnh lỗi (nếu có) đến email hỗ trợ để chúng tôi kiểm tra.</>,
  },
  {
    question: 'Vì sao không thấy truyện đã theo dõi?',
    answer: <>Hãy kiểm tra bạn đã đăng nhập đúng tài khoản, sau đó tải lại trang. Các tùy chọn chỉ lưu trong trình duyệt có thể thay đổi khi bạn xóa dữ liệu trang hoặc đổi thiết bị.</>,
  },
  {
    question: 'Tôi có thể tham gia hoặc đăng truyện với nhóm dịch không?',
    answer: <>Bạn có thể xem thông tin các nhóm tại <Link className="link" to={paths.houses}>Nhóm dịch</Link>. Việc quản lý và xuất bản nội dung yêu cầu tài khoản được phân quyền; vui lòng liên hệ ban quản trị nếu muốn hợp tác.</>,
  },
  {
    question: 'Tôi gặp lỗi đăng nhập hoặc quên mật khẩu thì sao?',
    answer: <>Trước tiên hãy kiểm tra email/tên đăng nhập và mật khẩu, sau đó thử lại. Nếu vẫn không thể truy cập, gửi email hỗ trợ từ địa chỉ liên quan đến tài khoản. Không gửi mật khẩu qua email.</>,
  },
  {
    question: 'Rồng Nhỏ có thu phí đọc truyện không?',
    answer: <>Các truyện công khai hiện được cung cấp để đọc trực tuyến, không có quy trình mua chương trong những tính năng đang được mô tả tại đây. Nếu có thay đổi, thông tin sẽ được công bố rõ trên website.</>,
  },
  {
    question: 'Làm sao báo cáo nội dung sai hoặc vi phạm bản quyền?',
    answer: <>Gửi URL chính xác, lý do phản ánh và thông tin có liên quan tới <a className="link" href={'mailto:' + EMAIL}>{EMAIL}</a>. Nếu yêu cầu dựa trên quyền tác giả, vui lòng nêu cơ sở chứng minh bạn là chủ thể quyền hoặc người được ủy quyền.</>,
  },
  {
    question: 'Website sử dụng cookie hoặc hiển thị quảng cáo không?',
    answer: <>Website dùng cookie và bộ nhớ trình duyệt để duy trì phiên và tùy chọn; đồng thời có thể hiển thị quảng cáo tùy cấu hình. Xem chi tiết tại <Link className="link" to={paths.privacy}>Chính sách bảo mật</Link>.</>,
  },
];

export function FaqPage() {
  return (
    <InfoPage title="Câu hỏi thường gặp (FAQ)" route={paths.faq}
      description="Giải đáp cách đọc truyện, tìm kiếm, theo dõi, xử lý lỗi, tham gia nhóm dịch và liên hệ hỗ trợ trên Rồng Nhỏ."
      intro="Tổng hợp những câu hỏi phổ biến khi sử dụng Rồng Nhỏ. Bấm vào từng câu hỏi để xem câu trả lời.">
      <section className="space-y-3" aria-label="Danh sách câu hỏi thường gặp">
        {questions.map(({ question, answer }) => (
          <details key={question} className="group rounded-2xl border border-line bg-surface px-5 py-4">
            <summary className="cursor-pointer font-semibold text-ink">{question}</summary>
            <div className="mt-3 border-t border-line pt-3 text-sm leading-7 text-muted sm:text-base">{answer}</div>
          </details>
        ))}
      </section>
    </InfoPage>
  );
}
